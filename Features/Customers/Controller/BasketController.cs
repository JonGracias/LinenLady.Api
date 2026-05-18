// Features/Customers/Controller/BasketController.cs

namespace LinenLady.API.Controllers;

using LinenLady.API.Auth;
using LinenLady.API.Contracts;
using LinenLady.API.Customers.Handler;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Customer-facing basket endpoints. Replaces the old /api/reservations
/// route — clients land on /account?tab=basket and call these directly.
/// The old /api/reservations POST is kept temporarily as a forwarding alias
/// (see ReservationsController) until the frontend cuts over.
/// </summary>
[ApiController]
[Authorize(Policy = AuthPolicies.Customer)]
[Route("api/customers/me/basket")]
public sealed class BasketController(
    GetBasketHandler        getHandler,
    AddToBasketHandler      addHandler,
    RemoveFromBasketHandler removeHandler,
    ReAddToBasketHandler    reAddHandler) : ControllerBase
{
    // GET /api/customers/me/basket
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var clerkUserId = User.GetClerkUserId();
        if (clerkUserId is null) return Unauthorized();

        return Ok(await getHandler.HandleAsync(clerkUserId, ct));
    }

    // POST /api/customers/me/basket/items
    [HttpPost("items")]
    public async Task<IActionResult> Add(
        [FromBody] AddToBasketRequest? body,
        CancellationToken ct)
    {
        var clerkUserId = User.GetClerkUserId();
        if (clerkUserId is null) return Unauthorized();
        if (body is null) return BadRequest("Invalid JSON body.");

        var created = await addHandler.HandleAsync(clerkUserId, body, ct);
        return StatusCode(201, created);
    }

    // DELETE /api/customers/me/basket/items/{reservationId}
    // Idempotent. Returns the now-Expired row so the UI can fold it into
    // the "recently expired" section without a second round-trip.
    [HttpDelete("items/{reservationId:int}")]
    public async Task<IActionResult> Remove(
        int reservationId, CancellationToken ct)
    {
        var clerkUserId = User.GetClerkUserId();
        if (clerkUserId is null) return Unauthorized();

        return Ok(await removeHandler.HandleAsync(clerkUserId, reservationId, ct));
    }

    // POST /api/customers/me/basket/items/{reservationId}/re-add
    // "Try again" affordance on a recently-expired row. Subject to all the
    // normal availability checks — returns 409 if someone else now holds
    // the piece.
    [HttpPost("items/{reservationId:int}/re-add")]
    public async Task<IActionResult> ReAdd(
        int reservationId, CancellationToken ct)
    {
        var clerkUserId = User.GetClerkUserId();
        if (clerkUserId is null) return Unauthorized();

        var added = await reAddHandler.HandleAsync(clerkUserId, reservationId, ct);
        return StatusCode(201, added);
    }
}

// ─────────────────────────────────────────────────────────────

/// <summary>
/// Checkout — one POST creates an order from selected basket items, returns
/// the order plus a Square payment link. The order starts in PaymentPending;
/// items are pulled from the basket immediately. If the customer abandons
/// Square's checkout, the timeout sweeper cancels the order in 24h and
/// recreates basket reservations where items are still available.
/// </summary>
[ApiController]
[Authorize(Policy = AuthPolicies.Customer)]
[Route("api/checkout")]
public sealed class CheckoutController(CheckoutHandler handler) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Submit(
        [FromBody] CheckoutRequest? body,
        CancellationToken ct)
    {
        var clerkUserId = User.GetClerkUserId();
        if (clerkUserId is null) return Unauthorized();
        if (body is null) return BadRequest("Invalid JSON body.");

        var order = await handler.HandleAsync(clerkUserId, body, ct);
        return StatusCode(201, order);
    }
}

// ─────────────────────────────────────────────────────────────

/// <summary>
/// Past + in-flight orders for the signed-in customer. Drives the new
/// /account?tab=orders pane.
/// </summary>
[ApiController]
[Authorize(Policy = AuthPolicies.Customer)]
[Route("api/customers/me/orders")]
public sealed class CustomerOrdersController(
    GetMyOrdersHandler   listHandler,
    GetOrderByIdHandler  detailHandler,
    CancelOrderHandler   cancelHandler) : ControllerBase
{
    // GET /api/customers/me/orders
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var clerkUserId = User.GetClerkUserId();
        if (clerkUserId is null) return Unauthorized();

        return Ok(await listHandler.HandleAsync(clerkUserId, ct));
    }

    // GET /api/customers/me/orders/{orderId}
    [HttpGet("{orderId:int}")]
    public async Task<IActionResult> Get(
        int orderId, CancellationToken ct)
    {
        var clerkUserId = User.GetClerkUserId();
        if (clerkUserId is null) return Unauthorized();

        return Ok(await detailHandler.HandleAsync(clerkUserId, orderId, ct));
    }

    // POST /api/customers/me/orders/{orderId}/cancel
    //
    // Customer-initiated cancel for PaymentPending (or Failed) orders.
    // Returns the updated order with Status = 'Cancelled'. The handler
    // throws OrderNotCancellableException for Paid orders so the global
    // exception filter can map it to a 409 with a structured body the
    // frontend uses to route the customer to the message-Noemi flow.
    [HttpPost("{orderId:int}/cancel")]
    public async Task<IActionResult> Cancel(
        int orderId, CancellationToken ct)
    {
        var clerkUserId = User.GetClerkUserId();
        if (clerkUserId is null) return Unauthorized();
        if (orderId <= 0) return BadRequest("Invalid order id.");

        return Ok(await cancelHandler.HandleAsync(clerkUserId, orderId, ct));
    }
}

// ─────────────────────────────────────────────────────────────

/// <summary>
/// "Ask Noemi about this piece" — start a per-item message thread tied to
/// a basket reservation. The handler creates the auto-greeting message
/// itself; the optional OpeningQuestion field lets the customer send their
/// first question in the same call.
/// </summary>
[ApiController]
[Authorize(Policy = AuthPolicies.Customer)]
[Route("api/customers/me/ask-noemi")]
public sealed class AskNoemiController(AskNoemiHandler handler) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Ask(
        [FromBody] AskNoemiRequest? body,
        CancellationToken ct)
    {
        var clerkUserId = User.GetClerkUserId();
        if (clerkUserId is null) return Unauthorized();
        if (body is null) return BadRequest("Invalid JSON body.");

        var message = await handler.HandleAsync(clerkUserId, body, ct);
        return StatusCode(201, message);
    }
}