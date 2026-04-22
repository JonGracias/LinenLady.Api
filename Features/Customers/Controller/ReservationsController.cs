namespace LinenLady.API.Controllers;

using LinenLady.API.Api.Auth;
using LinenLady.API.Contracts;
using LinenLady.API.Customers.Handler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Authorize(Policy = AuthPolicies.Customer)]
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
        var clerkUserId = User.GetClerkUserId();
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
        var clerkUserId = User.GetClerkUserId();
        if (clerkUserId is null) return Unauthorized();

        var result = await cancelHandler.HandleAsync(clerkUserId, reservationId, ct);
        return Ok(result);
    }

    // POST /square/webhook  (Square calls directly)
    // Signature verification is tracked as Severe #3 — the endpoint accepts
    // unauthenticated requests at the auth layer because there's no bearer
    // token from Square, but the handler itself should reject unsigned/invalid
    // payloads once verification is wired in.
    [AllowAnonymous]
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
