namespace LinenLady.Inventory.Api.Controllers;

using LinenLady.API.AI.Prefill.Service;
using LinenLady.API.Contracts;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("items/{id:int}")]
public sealed class AiPrefillController(IAiPrefillService service) : ControllerBase
{
    [HttpPost("ai-prefill")]
    [HttpPost("{field:regex(^(title|description|price)$)}/ai-prefill")]
    public async Task<IActionResult> Prefill(
        int id,
        string? field,
        [FromBody] AiPrefillRequest? request,
        CancellationToken ct)
    {
        var mode = field switch
        {
            "title" => PrefillMode.Title,
            "description" => PrefillMode.Description,
            "price" => PrefillMode.Price,
            _ => PrefillMode.All
        };

        var outcome = await service.PrefillAsync(id, mode, request ?? new(), ct);

        return outcome.Status switch
        {
            PrefillStatus.Ok => Ok(outcome.Item),
            PrefillStatus.NotFound => NotFound(outcome.ErrorMessage),
            PrefillStatus.NoImages
                or PrefillStatus.NoValidImages
                or PrefillStatus.InvalidBlobPaths => BadRequest(outcome.ErrorMessage),
            _ => StatusCode(500, "Unexpected outcome.")
        };
    }
}
