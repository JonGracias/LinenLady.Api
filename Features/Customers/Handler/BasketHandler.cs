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
    private readonly ILogger<ExpireStaleOrdersHandler> _log;
    private readonly int _timeoutHours;

    public ExpireStaleOrdersHandler(
        ICustomerRepository repo,
        IConfiguration config,
        ILogger<ExpireStaleOrdersHandler> log)
    {
        _repo = repo;
        _log  = log;
        // Tuneable so we can shorten in dev/QA; 24h matches Square's payment
        // link expiry default. Configured in appsettings as Checkout:OrderTimeoutHours.
        _timeoutHours = config.GetValue<int?>("Checkout:OrderTimeoutHours") ?? 24;
    }

    public async Task<int> HandleAsync(CancellationToken ct)
    {
        var count = await _repo.ExpireStaleOrdersAsync(_timeoutHours);
        if (count > 0)
            _log.LogInformation("Cancelled {Count} stale PaymentPending order(s).", count);
        return count;
    }
}
