namespace LinenLady.API.Controllers;

using LinenLady.API.Auth;
using LinenLady.API.Contracts;
using LinenLady.API.Site.Handler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/site/media")]
public sealed class SiteMediaController(SiteMediaHandler handler) : ControllerBase
{
    // GET /site/media  — public site
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await handler.ListAsync(ct);
        return Ok(result);
    }

    // POST /site/media
    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateMediaRequest? body,
        CancellationToken ct)
    {
        if (body is null) return BadRequest("Invalid body.");

        var result = await handler.CreateAsync(body, ct);
        return Ok(result);
    }

    // DELETE /site/media/{id}
    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var deleted = await handler.DeleteAsync(id, ct);
        return deleted ? NoContent() : NotFound();
    }
}
