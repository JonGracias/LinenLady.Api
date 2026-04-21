namespace LinenLady.API.Controllers;

using LinenLady.API.Contracts;
using LinenLady.API.Site.Handler;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("site/media")]
public sealed class SiteMediaController(SiteMediaHandler handler) : ControllerBase
{
    // GET /site/media
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await handler.ListAsync(ct);
        return Ok(result);
    }

    // POST /site/media
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
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var deleted = await handler.DeleteAsync(id, ct);
        return deleted ? NoContent() : NotFound();
    }
}
