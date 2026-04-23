namespace LinenLady.API.Controllers;

using LinenLady.API.Search.Handler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[AllowAnonymous]
[Route("api/items/{id:int}")]
public sealed class SearchController(SimilarItemsHandler handler) : ControllerBase
{
    // GET /items/{id}/similar?top=10&publishedOnly=true&minScore=0.0
    [HttpGet("similar")]
    public async Task<IActionResult> Similar(
        int id,
        [FromQuery] int top = 10,
        [FromQuery] bool publishedOnly = true,
        [FromQuery] double minScore = 0.0,
        CancellationToken ct = default)
    {
        if (id <= 0) return BadRequest("Invalid id.");

        var results = await handler.HandleAsync(id, top, publishedOnly, minScore, ct);
        return Ok(results);
    }
}
