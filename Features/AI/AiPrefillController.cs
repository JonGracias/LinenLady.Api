using LinenLady.API.AI.Service;
using LinenLady.API.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace LinenLady.Inventory.Api.Controllers;

[ApiController]
[Route("items/{id:int}")]
public sealed class AiPrefillController : ControllerBase
{
    private readonly IAiPrefillService _service;

    public AiPrefillController(IAiPrefillService service)
    {
        _service = service;
    }

    [HttpPost("ai-prefill")]
    public Task<IActionResult> PrefillAll(int id, [FromBody] AiPrefillRequest? request, CancellationToken ct)
        => Handle(id, PrefillMode.All, request, ct);

    [HttpPost("title/ai-prefill")]
    public Task<IActionResult> PrefillTitle(int id, [FromBody] AiPrefillRequest? request, CancellationToken ct)
        => Handle(id, PrefillMode.Title, request, ct);

    [HttpPost("description/ai-prefill")]
    public Task<IActionResult> PrefillDescription(int id, [FromBody] AiPrefillRequest? request, CancellationToken ct)
        => Handle(id, PrefillMode.Description, request, ct);

    [HttpPost("price/ai-prefill")]
    public Task<IActionResult> PrefillPrice(int id, [FromBody] AiPrefillRequest? request, CancellationToken ct)
        => Handle(id, PrefillMode.Price, request, ct);

    private async Task<IActionResult> Handle(
        int id,
        PrefillMode mode,
        AiPrefillRequest? request,
        CancellationToken ct)
    {
        var outcome = await _service.PrefillAsync(id, mode, request ?? new AiPrefillRequest(), ct);

        return outcome.Status switch
        {
            PrefillStatus.Ok => Ok(outcome.Item),
            PrefillStatus.NotFound => NotFound(outcome.ErrorMessage),
            PrefillStatus.NoImages => BadRequest(outcome.ErrorMessage),
            PrefillStatus.NoValidImages => BadRequest(outcome.ErrorMessage),
            PrefillStatus.InvalidBlobPaths => BadRequest(outcome.ErrorMessage),
            _ => StatusCode(500, "Unexpected outcome.")
        };
    }
}
