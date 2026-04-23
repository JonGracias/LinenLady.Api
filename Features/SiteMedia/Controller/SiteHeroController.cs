namespace LinenLady.API.Controllers;

using LinenLady.API.Api.Auth;
using LinenLady.API.Contracts;
using LinenLady.API.Site.Handler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/site/hero")]
public sealed class SiteHeroController(SiteHeroHandler handler) : ControllerBase
{
    // GET /site/hero?activeOnly=true  — public site
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] bool activeOnly = false,
        CancellationToken ct = default)
    {
        var result = await handler.ListAsync(activeOnly, ct);
        return Ok(result);
    }

    // POST /site/hero
    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] UpsertHeroSlideRequest? body,
        CancellationToken ct)
    {
        if (body is null) return BadRequest("Invalid body.");

        var result = await handler.CreateAsync(body, ct);
        return StatusCode(201, result);
    }

    // PATCH /site/hero/reorder
    // Declared before the {id:int} route — literal paths always win over
    // constrained params, but being explicit prevents accidents later.
    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpPatch("reorder")]
    public async Task<IActionResult> Reorder(
        [FromBody] ReorderHeroSlidesRequest? body,
        CancellationToken ct)
    {
        if (body is null || body.Slides.Count == 0)
            return BadRequest("Invalid body.");

        await handler.ReorderAsync(body.Slides, ct);
        return NoContent();
    }

    // PATCH /site/hero/{id}
    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpPatch("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpsertHeroSlideRequest? body,
        CancellationToken ct)
    {
        if (body is null) return BadRequest("Invalid body.");

        var result = await handler.UpdateAsync(id, body, ct);
        return result is null ? NotFound() : Ok(result);
    }

    // DELETE /site/hero/{id}
    [Authorize(Policy = AuthPolicies.Admin)]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var deleted = await handler.DeleteAsync(id, ct);
        return deleted ? NoContent() : NotFound();
    }
}
