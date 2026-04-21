namespace LinenLady.API.Controllers;

using LinenLady.API.Api.Auth;
using LinenLady.API.Contracts;
using LinenLady.API.Customers.Handler;
using Microsoft.AspNetCore.Mvc;

[ApiController]
public sealed class ReservationsController(
    CreateReservationHandler createHandler,
    CancelReservationHandler cancelHandler,
    SquareWebhookHandler webhookHandler) : ControllerBase
{
    // POST /reservations
    [HttpPost("reservations")]
    public async Task<IActionResult> Create(
        [FromBody] CreateReservationRequest? body,
        CancellationToken ct)
    {
        var clerkUserId = Request.GetClerkUserId();
        if (clerkUserId is null) return Unauthorized();
        if (body is null) return BadRequest("Invalid JSON body.");

        var result = await createHandler.HandleAsync(clerkUserId, body, ct);
        return StatusCode(201, result);
    }

    // PATCH /reservations/{id}/cancel
    [HttpPatch("reservations/{reservationId:int}/cancel")]
    public async Task<IActionResult> Cancel(
        int reservationId,
        CancellationToken ct)
    {
        var clerkUserId = Request.GetClerkUserId();
        if (clerkUserId is null) return Unauthorized();

        var result = await cancelHandler.HandleAsync(clerkUserId, reservationId, ct);
        return Ok(result);
    }

    // POST /square/webhook  (no auth — Square calls directly)
    // NOTE: This endpoint should validate the Square-Signature header against
    // a configured webhook signing key before trusting the payload. Currently
    // matches existing behavior (no validation), but flagged for follow-up.
    [HttpPost("square/webhook")]
    public async Task<IActionResult> SquareWebhook(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);

        if (string.IsNullOrWhiteSpace(body))
            return BadRequest();

        await webhookHandler.HandleAsync(body, ct);
        return Ok();
    }
}
