namespace LinenLady.API.Controllers;

using LinenLady.API.Contracts;
using LinenLady.API.Inventory.AiMeta.Sql;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("items/{id:int}/ai-meta")]
public sealed class InventoryAiMetaController(IInventoryAiMetaRepository repo) : ControllerBase
{
    // GET /items/{id}/ai-meta
    [HttpGet]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        if (id <= 0) return BadRequest("Invalid id.");

        // Matches the old Function: returns an empty scaffold rather than 404
        // when no meta row exists yet, so the frontend can bind against a
        // consistent shape either way.
        var row = await repo.GetAsync(id, ct) ?? new AiMetaRow(null, null, null, null, null, null);
        return Ok(row);
    }

    // PATCH /items/{id}/ai-meta/notes
    [HttpPatch("notes")]
    public async Task<IActionResult> UpsertNotes(
        int id,
        [FromBody] UpsertAdminNotesRequest? body,
        CancellationToken ct)
    {
        if (id <= 0) return BadRequest("Invalid id.");
        if (body is null) return BadRequest("Invalid JSON body.");

        await repo.UpsertAdminNotesAsync(id, body.AdminNotes, ct);
        return Ok(new { ok = true });
    }
}
