namespace LinenLady.Inventory.Api.Controllers;

using LinenLady.API.AI.Embeddings.Service;
using LinenLady.API.Contracts;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("items/{id:int}")]
public sealed class AiEmbeddingsController(IAiEmbeddingsService service) : ControllerBase
{
    [HttpPost("vectors/refresh")]
    public async Task<IActionResult> RefreshVector(
        int id,
        [FromBody] RefreshVectorRequest? request,
        CancellationToken ct)
    {
        var outcome = await service.RefreshAsync(id, request ?? new(), ct);

        return outcome.Status switch
        {
            RefreshVectorStatus.Ok => Ok(outcome.Result),
            RefreshVectorStatus.NotFound => NotFound(outcome.ErrorMessage),
            RefreshVectorStatus.InvalidId
                or RefreshVectorStatus.PurposeTooLong
                or RefreshVectorStatus.NoTextToEmbed => BadRequest(outcome.ErrorMessage),
            _ => StatusCode(500, "Unexpected outcome.")
        };
    }
}
