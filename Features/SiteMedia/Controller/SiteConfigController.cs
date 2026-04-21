namespace LinenLady.API.Controllers;

using LinenLady.API.Contracts;
using LinenLady.API.Site.Handler;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("site/config")]
public sealed class SiteConfigController(SiteConfigHandler handler) : ControllerBase
{
    // GET /site/config
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await handler.ListAsync(ct);
        return Ok(result);
    }

    // GET /site/config/{key}
    [HttpGet("{key}")]
    public async Task<IActionResult> Get(string key, CancellationToken ct)
    {
        var result = await handler.GetAsync(key, ct);
        return result is null
            ? NotFound($"Config key '{key}' not found.")
            : Ok(result);
    }

    // PUT /site/config/{key}
    [HttpPut("{key}")]
    public async Task<IActionResult> Set(
        string key,
        [FromBody] SetConfigRequest? body,
        CancellationToken ct)
    {
        if (body is null) return BadRequest("Invalid body.");

        var result = await handler.SetAsync(key, body.MediaId, ct);
        return Ok(result);
    }
}
