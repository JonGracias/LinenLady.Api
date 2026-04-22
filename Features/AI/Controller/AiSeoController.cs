namespace LinenLady.API.Controllers;

using LinenLady.API.AI.Seo.Service;
using LinenLady.API.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize(Policy = AuthPolicies.Admin)]
[Route("items/{id:int}")]
public sealed class AiSeoController(IAiSeoService service) : ControllerBase
{
    [HttpPost("seo/generate")]
    public async Task<IActionResult> Generate(int id, CancellationToken ct)
    {
        if (id <= 0) return BadRequest("Invalid id.");

        var result = await service.GenerateAsync(id, ct);
        return Ok(result);
    }
}
