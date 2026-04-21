namespace LinenLady.API.Controllers;

using LinenLady.API.AI.Keywords.Service;
using LinenLady.API.Contracts;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("items/{id:int}")]
public sealed class AiKeywordsController(IAiKeywordsService service) : ControllerBase
{
    [HttpPost("keywords/generate")]
    public async Task<IActionResult> Generate(
        int id,
        [FromBody] GenerateKeywordsRequest? body,
        CancellationToken ct)
    {
        if (id <= 0) return BadRequest("Invalid id.");

        var result = await service.GenerateAsync(id, body?.Hint, ct);
        return Ok(result);
    }
}
