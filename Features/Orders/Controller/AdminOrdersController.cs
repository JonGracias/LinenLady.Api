namespace LinenLady.API.Controllers;

using LinenLady.API.Auth;
using LinenLady.API.Contracts;
using LinenLady.API.Features.Orders.Handler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize(Policy = AuthPolicies.Admin)]
[Route("api/admin/orders")]
public sealed class AdminOrdersController(AdminOrdersHandler handler) : ControllerBase
{
    // GET /api/admin/orders — every order, newest first
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await handler.GetAll());

    // GET /api/admin/orders/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var detail = await handler.GetById(id, ct);
        return detail is null ? NotFound("Order not found.") : Ok(detail);
    }

    // POST /api/admin/orders/{id}/checkpoint
    // Body: { "checkpoint": "shipped" | "delivered" | "returned", "clear": false }
    // Returns the updated order detail.
    [HttpPost("{id:int}/checkpoint")]
    public async Task<IActionResult> SetCheckpoint(
        int id,
        [FromBody] SetOrderCheckpointRequest? body,
        CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Checkpoint))
            return BadRequest("Checkpoint is required.");

        var (ok, error, detail) = await handler.SetCheckpoint(id, body, ct);
        if (!ok)
            return error == "Order not found." ? NotFound(error) : BadRequest(error);

        return Ok(detail);
    }
}
