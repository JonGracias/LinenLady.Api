// Features/Customers/Handler/BasketHandler.cs
//
// Handlers for the basket / order / checkout surface. Lives alongside the
// existing CustomerHandler.cs — same exception taxonomy, same DI shape,
// same conventions.

namespace LinenLady.API.Customers.Handler;

using LinenLady.API.Contracts;
using LinenLady.API.Customers.Sql;
using LinenLady.API.Square;

// New exceptions (additions to the set in CustomerHandler.cs)
public sealed class AddressNotFoundException : Exception
{
    public AddressNotFoundException(string m) : base(m) {}
}

public sealed class OrderNotFoundException : Exception
{
    public OrderNotFoundException(string m) : base(m) {}
}

/// <summary>
/// Customer tried to cancel an order that isn't in a cancellable state.
/// Distinct from OrderNotFoundException because the order DOES exist and
/// is theirs — they just can't cancel it via the self-serve endpoint. The
/// most common case is "the Square webhook landed before your cancel POST
/// did, so this order is now Paid and you need to message Noemi for a
/// refund instead." Frontend uses OrderStatus to route the customer to
/// the message thread when the cause is Paid.
/// </summary>
public sealed class OrderNotCancellableException : Exception
{
    public string OrderStatus { get; }
    public OrderNotCancellableException(string status, string m) : base(m)
    {
        OrderStatus = status;
    }
}

// ─────────────────────────────────────────────────────────────
// Basket: get + add + remove + re-add
// ─────────────────────────────────────────────────────────────

public sealed class GetBasketHandler
{
    private readonly ICustomerRepository _repo;
    public GetBasketHandler(ICustomerRepository repo) => _repo = repo;

    public async Task<List<ReservationDto>> HandleAsync(string clerkUserId, CancellationToken ct)
    {
        var customer = await _repo.GetByClerkIdAsync(clerkUserId)
            ?? throw new CustomerNotFoundException("Profile not found.");

        return await _repo.GetBasketAsync(customer.CustomerId);
    }
}

public sealed class AddToBasketHandler
{
    private readonly ICustomerRepository _repo;
    private readonly ILogger<AddToBasketHandler> _log;

    public AddToBasketHandler(ICustomerRepository repo, ILogger<AddToBasketHandler> log)
    {
        _repo = repo;
        _log  = log;
    }

    public async Task<ReservationDto> HandleAsync(
        string clerkUserId, AddToBasketRequest req, CancellationToken ct)
    {
        var customer = await _repo.GetByClerkIdAsync(clerkUserId)
            ?? throw new CustomerNotFoundException("Profile not found.");

        if (!customer.IsEmailVerified)
            throw new EmailNotVerifiedException(
                "Email verification required before adding to your basket.");

        // Check who-has-it before attempting the insert. The repo's
        // INSERT-WHERE-NOT-EXISTS is race-safe against two strangers, but
        // we still need this look-up so we can return the
        // ItemAlreadyReservedByYouException variant when *the same* customer
        // already holds the item — frontend uses it to redirect to the
        // basket rather than show an error.
        var existing = await _repo.GetActiveReservationForItemAsync(req.InventoryId);
        if (existing is not null)
        {
            if (existing.CustomerId == customer.CustomerId)
                throw new ItemAlreadyReservedByYouException(
                    existing.ReservationId,
                    "You already have this piece in your basket.");

            throw new ItemAlreadyReservedException(
                "Another customer is currently considering this piece.");
        }

        var created = await _repo.CreateBasketItemAsync(customer.CustomerId, req);

        // Null = the conditional INSERT didn't fire. Either someone won
        // a race in the millisecond since GetActiveReservation, or the
        // item failed the IsActive/IsDraft/IsDeleted gate. Treat as a
        // generic conflict; frontend re-fetches and sorts itself out.
        if (created is null)
            throw new ItemAlreadyReservedException(
                "This piece just became unavailable. Try refreshing the page.");

        _log.LogInformation(
            "Basket add: customer {CustomerId}, inventory {InventoryId}, reservation {ReservationId}",
            customer.CustomerId, req.InventoryId, created.ReservationId);

        return created;
    }
}

public sealed class RemoveFromBasketHandler
{
    private readonly ICustomerRepository _repo;
    public RemoveFromBasketHandler(ICustomerRepository repo) => _repo = repo;

    public async Task<ReservationDto> HandleAsync(
        string clerkUserId, int reservationId, CancellationToken ct)
    {
        var customer = await _repo.GetByClerkIdAsync(clerkUserId)
            ?? throw new CustomerNotFoundException("Profile not found.");

        var updated = await _repo.RemoveBasketItemAsync(customer.CustomerId, reservationId);

        // Null on remove can mean: not the customer's reservation, doesn't
        // exist, or already Expired. In all three cases there's nothing
        // for us to do but tell the customer. We lean toward 404 vs 409
        // because "remove this thing that isn't in my basket" is more
        // ergonomically a Not-Found than a Conflict.
        if (updated is null)
            throw new ReservationNotFoundException("That basket item couldn't be found.");

        return updated;
    }
}

public sealed class ReAddToBasketHandler
{
    private readonly ICustomerRepository _repo;
    public ReAddToBasketHandler(ICustomerRepository repo) => _repo = repo;

    public async Task<ReservationDto> HandleAsync(
        string clerkUserId, int reservationId, CancellationToken ct)
    {
        var customer = await _repo.GetByClerkIdAsync(clerkUserId)
            ?? throw new CustomerNotFoundException("Profile not found.");

        var added = await _repo.ReAddBasketItemAsync(customer.CustomerId, reservationId);
        if (added is null)
            throw new ItemAlreadyReservedException(
                "This piece is no longer available — it may have sold or been " +
                "added to someone else's basket.");

        return added;
    }
}

// ─────────────────────────────────────────────────────────────
// Checkout
// ─────────────────────────────────────────────────────────────

public sealed class CheckoutHandler
{
    private readonly ICustomerRepository _repo;
    private readonly ISquareService _square;
    private readonly ILogger<CheckoutHandler> _log;

    public CheckoutHandler(
        ICustomerRepository repo,
        ISquareService square,
        ILogger<CheckoutHandler> log)
    {
        _repo   = repo;
        _square = square;
        _log    = log;
    }

    public async Task<OrderDto> HandleAsync(
        string clerkUserId, CheckoutRequest req, CancellationToken ct)
    {
        if (req.ReservationIds is null || req.ReservationIds.Count == 0)
            throw new ArgumentException("Select at least one item to check out.");

        var customer = await _repo.GetByClerkIdAsync(clerkUserId)
            ?? throw new CustomerNotFoundException("Profile not found.");

        if (!customer.IsEmailVerified)
            throw new EmailNotVerifiedException(
                "Email verification required before checkout.");

        // CheckoutAsync is the single transactional step that creates the
        // Order, snapshots the address, creates OrderItems, and flips the
        // reservations to Expired. By the time it returns, the basket no
        // longer holds these items — even if the Square call below fails.
        // If Square fails the order goes to the timeout sweeper, which will
        // recreate basket reservations where possible (#1 design choice).
        var order = await _repo.CheckoutAsync(customer.CustomerId, req);

        // Generate the Square payment link for the full order. New overload
        // takes an OrderDto with line items; old single-reservation overload
        // is unchanged in case anything still calls it (currently nothing
        // post-migration, but harmless to leave).
        try
        {
            var link = await _square.CreatePaymentLinkForOrderAsync(
                order:         order,
                customerEmail: customer.Email,
                customerName:  $"{customer.FirstName} {customer.LastName}".Trim());

            order = await _repo.SetOrderPaymentLinkAsync(order.OrderId, link) ?? order;

            await _repo.LogNotificationAsync(
                customer.CustomerId, reservationId: null, "PaymentLinkSent", true);
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "Square payment link generation failed for order {OrderId} (non-fatal — sweeper will recover).",
                order.OrderId);
            await _repo.LogNotificationAsync(
                customer.CustomerId, reservationId: null,
                "PaymentLinkSent", false, ex.Message);

            // We deliberately don't re-throw or roll back. The order exists,
            // the items are out of the basket, and the timeout sweeper will
            // cancel the order in N hours and put items back. Surfacing a
            // 500 here would lie about the order state — it really did get
            // created. Frontend handles missing SquarePaymentLinkUrl by
            // showing a "we'll email you a payment link" message.
        }

        // Order-level auto-message (#4: per-order, not per-item).
        try
        {
            var lines = string.Join("\n",
                order.Items.Select(i =>
                    $"  • {i.ItemName}" +
                    (string.IsNullOrWhiteSpace(i.ItemSku) ? "" : $" (SKU {i.ItemSku})") +
                    $" — ${i.UnitPriceCents / 100m:0.00}"));

            var addr = $"{order.ShipStreet1}, {order.ShipCity}, {order.ShipState} {order.ShipZip}";
            var body =
                $"Order #{order.OrderId} placed:\n{lines}\n\n" +
                $"Total: ${order.AmountCents / 100m:0.00}\n" +
                $"Shipping to: {addr}";

            await _repo.SendMessageAsync(
                customer.CustomerId,
                new SendMessageRequest(body, ReservationId: null, OrderId: order.OrderId),
                direction: "Inbound");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "Auto-message for order {OrderId} failed (non-fatal).",
                order.OrderId);
        }

        _log.LogInformation(
            "Checkout: order {OrderId}, customer {CustomerId}, {ItemCount} items, ${Total}",
            order.OrderId, customer.CustomerId, order.Items.Count, order.AmountCents / 100m);

        return order;
    }
}

// ─────────────────────────────────────────────────────────────
// Order queries
// ─────────────────────────────────────────────────────────────

public sealed class GetMyOrdersHandler
{
    private readonly ICustomerRepository _repo;
    public GetMyOrdersHandler(ICustomerRepository repo) => _repo = repo;

    public async Task<List<OrderDto>> HandleAsync(string clerkUserId, CancellationToken ct)
    {
        var customer = await _repo.GetByClerkIdAsync(clerkUserId)
            ?? throw new CustomerNotFoundException("Profile not found.");

        return await _repo.GetCustomerOrdersAsync(customer.CustomerId);
    }
}

public sealed class GetOrderByIdHandler
{
    private readonly ICustomerRepository _repo;
    public GetOrderByIdHandler(ICustomerRepository repo) => _repo = repo;

    public async Task<OrderDto> HandleAsync(
        string clerkUserId, int orderId, CancellationToken ct)
    {
        var customer = await _repo.GetByClerkIdAsync(clerkUserId)
            ?? throw new CustomerNotFoundException("Profile not found.");

        var order = await _repo.GetOrderAsync(orderId)
            ?? throw new OrderNotFoundException("Order not found.");

        // Same-customer check — defense in depth. Without this, OrderId is
        // an enumerable integer that any signed-in user could probe to
        // read someone else's order.
        if (order.CustomerId != customer.CustomerId)
            throw new OrderNotFoundException("Order not found.");

        return order;
    }
}

// ─────────────────────────────────────────────────────────────
// Cancel a PaymentPending order (customer-initiated)
// ─────────────────────────────────────────────────────────────
//
// Customer-initiated cancel for orders that haven't been paid yet. Money
// hasn't moved (the order is PaymentPending), so this is purely a status
// flip + inventory recovery. Paid orders are NOT cancellable through this
// path — the customer must message Noemi for a manual refund.
//
// The actual work is done by the existing _repo.CancelOrderAsync method,
// which the timeout sweeper also calls. We're effectively exposing one
// row of the sweeper's work to the customer, scoped by ownership.
//
// CancelOrderAsync is already race-safe — its UPDATE has WHERE Status =
// 'PaymentPending' so only one of three concurrent writers wins:
//   1. This handler (customer clicked Cancel)
//   2. ExpireStaleOrdersHandler (hourly sweeper)
//   3. SquareWebhookHandler (Square POSTs payment.updated)
//
// If we lose the race we re-read the order to find out who won and surface
// the right error.
//
// Bonus over the original spec: CancelOrderAsync ALSO recreates the
// customer's basket reservations for items still purchasable. So a
// customer who cancels and changes their mind sees the pieces back in
// their basket immediately, no /shop re-add needed. Better UX than a
// pure "release" would have given us.

public sealed class CancelOrderHandler
{
    private readonly ICustomerRepository _repo;
    private readonly ISquareService _square;
    private readonly ILogger<CancelOrderHandler> _log;

    public CancelOrderHandler(
        ICustomerRepository repo,
        ISquareService square,
        ILogger<CancelOrderHandler> log)
    {
        _repo   = repo;
        _square = square;
        _log    = log;
    }

    public async Task<OrderDto> HandleAsync(
        string clerkUserId, int orderId, CancellationToken ct)
    {
        var customer = await _repo.GetByClerkIdAsync(clerkUserId)
            ?? throw new CustomerNotFoundException("Profile not found.");

        // Ownership check via a read-only fetch. Same pattern as
        // GetOrderByIdHandler — OrderId is enumerable, so we 404 rather
        // than 403 on cross-customer probes to avoid revealing existence.
        var order = await _repo.GetOrderAsync(orderId)
            ?? throw new OrderNotFoundException("Order not found.");

        if (order.CustomerId != customer.CustomerId)
            throw new OrderNotFoundException("Order not found.");

        // Pre-flight status check. Cheap, gives a clearer error than
        // letting the repo's UPDATE no-op silently. CancelOrderAsync
        // accepts 'Cancelled' and 'Failed' as newStatus, but it only
        // moves rows currently in 'PaymentPending' — its WHERE clause
        // is the gatekeeper, not the newStatus value.
        switch (order.Status)
        {
            case "Cancelled":
                // Idempotent success — the order is already in the state
                // the customer wants. Return the current row.
                _log.LogInformation(
                    "Order {OrderId} cancel: already Cancelled (idempotent).", orderId);
                return order;

            case "Paid":
                throw new OrderNotCancellableException(
                    order.Status,
                    "This order has already been paid. Message Noemi to request a refund.");

            case "PaymentPending":
                // The normal path. Fall through to the repo call.
                break;

            case "Failed":
                // Edge case: an order that already failed via webhook
                // doesn't need a customer-initiated cancel — there's
                // nothing to cancel. Treat as idempotent success and
                // return the row.
                _log.LogInformation(
                    "Order {OrderId} cancel: status Failed, nothing to cancel.", orderId);
                return order;

            default:
                // Defensive: any future status we don't know about.
                throw new OrderNotCancellableException(
                    order.Status,
                    $"This order is in an unexpected state ({order.Status}) and " +
                    "can't be cancelled. Message Noemi for help.");
        }

        // Hand off to the existing repo method.
        //
        // Sequencing matters here and is deliberate (see #cancel-ordering):
        //   1. The repo cancels the order in the DB. That transaction
        //      COMMITS before we touch Square — local state is the source
        //      of truth and must never depend on Square being reachable.
        //   2. If we won the cancel (non-null result), we then revoke the
        //      Square payment link best-effort.
        //   3. A revoke failure is LOGGED AS AN ERROR but does not fail the
        //      cancellation — the order is already cancelled; the stale link
        //      is a known hazard the webhook handler now alerts on if anyone
        //      actually pays it.
        //
        // If cancelResult is null we lost a race; the post-read below
        // figures out who won. The sweeper revokes its own links, so we
        // don't need to chase the link in that case.
        var cancelResult = await _repo.CancelOrderAsync(orderId, "Cancelled");

        if (cancelResult is not null &&
            !string.IsNullOrWhiteSpace(cancelResult.SquarePaymentLinkId))
        {
            try
            {
                await _square.DeletePaymentLinkAsync(cancelResult.SquarePaymentLinkId, ct);
                _log.LogInformation(
                    "Square payment link {LinkId} revoked for cancelled order {OrderId}.",
                    cancelResult.SquarePaymentLinkId, orderId);
            }
            catch (Exception ex)
            {
                _log.LogError(ex,
                    "Square payment-link revoke FAILED for cancelled order {OrderId} " +
                    "(link {LinkId}). Order remains cancelled; the stale link can still " +
                    "accept payment until manually deleted in the Square dashboard.",
                    orderId, cancelResult.SquarePaymentLinkId);
            }
        }

        // Re-fetch to get the post-state. Three cases:
        //   1. Our cancel succeeded — Status = 'Cancelled', return it.
        //   2. Webhook beat us — Status = 'Paid', throw Paid exception.
        //   3. Sweeper beat us — Status = 'Cancelled', idempotent success.
        //
        // The repo's UPDATE doesn't tell us which (the affected count is
        // 0 in both 2 and 3), so we re-read.
        var afterCancel = await _repo.GetOrderAsync(orderId)
            ?? throw new OrderNotFoundException("Order not found.");

        if (afterCancel.Status == "Paid")
            throw new OrderNotCancellableException(
                afterCancel.Status,
                "Your payment just went through — this order can no longer " +
                "be cancelled. Message Noemi to request a refund.");

        if (afterCancel.Status != "Cancelled")
            // Shouldn't reach here under any normal flow — log and surface
            // a generic error rather than returning a confused state.
            throw new OrderNotCancellableException(
                afterCancel.Status,
                "This order couldn't be cancelled. Message Noemi for help.");

        // Post a Noemi-thread message so the admin side has a trail of
        // the cancel without needing a separate "cancellations" inbox.
        // Mirrors the pattern in CheckoutHandler.HandleAsync. Best-effort —
        // a message-send failure doesn't roll back the cancel.
        try
        {
            var lines = string.Join("\n",
                afterCancel.Items.Select(i =>
                    $"  • {i.ItemName}" +
                    (string.IsNullOrWhiteSpace(i.ItemSku) ? "" : $" (SKU {i.ItemSku})")));

            var body =
                $"Order #{afterCancel.OrderId} cancelled by customer.\n{lines}\n\n" +
                "Pieces still available have been returned to your basket.";

            await _repo.SendMessageAsync(
                customer.CustomerId,
                new SendMessageRequest(body, ReservationId: null, OrderId: afterCancel.OrderId),
                direction: "Inbound");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "Cancel auto-message for order {OrderId} failed (non-fatal).",
                afterCancel.OrderId);
        }

        _log.LogInformation(
            "Order {OrderId} cancelled by customer {CustomerId} ({ItemCount} items).",
            afterCancel.OrderId, customer.CustomerId, afterCancel.Items.Count);

        return afterCancel;
    }
}

// ─────────────────────────────────────────────────────────────
// "Ask Noemi about this piece" (#4 per-item thread starter)
// ─────────────────────────────────────────────────────────────

public sealed class AskNoemiHandler
{
    private readonly ICustomerRepository _repo;
    public AskNoemiHandler(ICustomerRepository repo) => _repo = repo;

    public async Task<MessageDto> HandleAsync(
        string clerkUserId, AskNoemiRequest req, CancellationToken ct)
    {
        var customer = await _repo.GetByClerkIdAsync(clerkUserId)
            ?? throw new CustomerNotFoundException("Profile not found.");

        // Verify the reservation belongs to this customer — else they could
        // start a thread referencing someone else's reservation.
        var basket = await _repo.GetBasketAsync(customer.CustomerId);
        var item = basket.FirstOrDefault(r => r.ReservationId == req.ReservationId)
            ?? throw new ReservationNotFoundException("Basket item not found.");

        // Auto-greeting tagged with the reservation id so the admin sees
        // what piece the question is about, then the customer's actual
        // question (if provided) as a follow-up message.
        var opener = await _repo.SendMessageAsync(
            customer.CustomerId,
            new SendMessageRequest(
                Body: $"Question about: {item.ItemName ?? "Linen Lady piece"}" +
                      (string.IsNullOrWhiteSpace(item.ItemSku) ? "" : $" (SKU {item.ItemSku})"),
                ReservationId: item.ReservationId,
                OrderId: null),
            direction: "Inbound");

        if (!string.IsNullOrWhiteSpace(req.OpeningQuestion))
        {
            return await _repo.SendMessageAsync(
                customer.CustomerId,
                new SendMessageRequest(
                    Body: req.OpeningQuestion!.Trim(),
                    ReservationId: item.ReservationId,
                    OrderId: null),
                direction: "Inbound");
        }

        return opener;
    }
}

// ─────────────────────────────────────────────────────────────
// Background sweeper for timed-out PaymentPending orders
// ─────────────────────────────────────────────────────────────

public sealed class ExpireStaleOrdersHandler
{
    private readonly ICustomerRepository _repo;
    private readonly ISquareService _square;
    private readonly ILogger<ExpireStaleOrdersHandler> _log;
    private readonly int _timeoutHours;

    public ExpireStaleOrdersHandler(
        ICustomerRepository repo,
        ISquareService square,
        IConfiguration config,
        ILogger<ExpireStaleOrdersHandler> log)
    {
        _repo   = repo;
        _square = square;
        _log    = log;
        // Tuneable so we can shorten in dev/QA. NOTE: Square payment links
        // do NOT expire on their own — this timeout is purely ours, which
        // is exactly why each sweep revokes the link below. Configured in
        // appsettings as Checkout:OrderTimeoutHours.
        _timeoutHours = config.GetValue<int?>("Checkout:OrderTimeoutHours") ?? 24;
    }

    public async Task<int> HandleAsync(CancellationToken ct)
    {
        // DB first: every order in the returned list is ALREADY cancelled
        // and committed by the time we see it here. Square cleanup is a
        // best-effort second pass — a Square outage can delay link
        // revocation but can never block or roll back a cancellation.
        var cancelled = await _repo.ExpireStaleOrdersAsync(_timeoutHours);
        if (cancelled.Count == 0)
            return 0;

        _log.LogInformation(
            "Cancelled {Count} stale PaymentPending order(s).", cancelled.Count);

        foreach (var order in cancelled)
        {
            if (string.IsNullOrWhiteSpace(order.SquarePaymentLinkId))
                continue; // link generation failed at checkout — nothing to revoke

            try
            {
                await _square.DeletePaymentLinkAsync(order.SquarePaymentLinkId, ct);
                _log.LogInformation(
                    "Square payment link {LinkId} revoked for swept order {OrderId}.",
                    order.SquarePaymentLinkId, order.OrderId);
            }
            catch (Exception ex)
            {
                // Log loudly and keep sweeping — one bad link must not stop
                // the rest of the batch. The next tick will NOT retry this
                // link (the order is no longer PaymentPending), so this log
                // line is the alert: the stale link can still take payment
                // until deleted in the Square dashboard. The webhook
                // handler's payment-after-cancel alert is the backstop.
                _log.LogError(ex,
                    "Square payment-link revoke FAILED for swept order {OrderId} " +
                    "(link {LinkId}). Delete it manually in the Square dashboard.",
                    order.OrderId, order.SquarePaymentLinkId);
            }
        }

        return cancelled.Count;
    }
}