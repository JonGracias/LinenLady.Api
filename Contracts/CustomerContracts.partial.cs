namespace LinenLady.API.Contracts;

// ── Reservation (basket holds) ────────────────────────────────
//
// A reservation row IS a basket entry. Status collapses to:
//   Active  — currently in the customer's basket
//   Expired — gone (timed out OR removed by customer OR consumed by an Order)
//
// The lifecycle:
//   POST   /api/basket/items              → creates Active row
//   DELETE /api/basket/items/{id}         → flips to Expired (manual remove)
//   POST   /api/checkout                  → flips checked rows to Expired
//                                            and creates Order/OrderItems
//   ExpireReservationsBackgroundService   → flips timed-out rows to Expired

public record ReservationDto(
    int       ReservationId,
    int       CustomerId,
    int       InventoryId,
    string    Status,             // Active | Expired
    DateTime  ReservedAt,
    DateTime  ExpiresAt,
    string?   CustomerNotes,

    // Inventory snapshot — denormalized for the basket UI
    string?   ItemName,
    string?   ItemSku,
    Guid?     ItemPublicId,
    string?   ThumbnailUrl,
    int       UnitPriceCents,

    // For "recently expired" UX (#3 in the design): is the underlying
    // inventory item still purchasable, so the customer can re-add it?
    // The repo computes this from inv.Inventory.IsActive/IsDraft/IsDeleted
    // AND absence of a competing Active reservation. Always false for Active
    // rows (you already have it).
    bool      CanReAdd
);

// Replaces the old CreateReservationRequest. Same shape minus the rename;
// kept distinct so the API surface name matches the new mental model.
public record AddToBasketRequest(
    int     InventoryId,
    string? CustomerNotes
);

// ── Order ─────────────────────────────────────────────────────
//
// Created at checkout. Address is snapshotted from cust.CustomerAddress
// at submission time — editing the saved address later doesn't mutate
// historical orders.
//
// Items is intentionally NOT a constructor parameter. Dapper materializes
// records via positional ctor matching, and the cust.[Order] table doesn't
// (and can't) carry an Items column. The repository (GetCustomerOrdersAsync,
// HydrateOrderAsync) populates Items via a second query against cust.OrderItem
// and attaches it via `o with { Items = ... }`. Default value of an empty
// list lets callers that don't hydrate items access .Items without NRE.

public record OrderDto(
    int       OrderId,
    int       CustomerId,
    string    Status,             // PaymentPending | Paid | Cancelled | Failed
    int       AmountCents,
    string?   SquarePaymentLinkUrl,
    string?   SquareOrderId,

    // Shipping snapshot
    string?   ShipLabel,
    string?   ShipStreet1,
    string?   ShipStreet2,
    string?   ShipCity,
    string?   ShipState,
    string?   ShipZip,
    string?   ShipCountry,

    string?   CustomerNotes,
    DateTime  CreatedAt,
    DateTime? PaidAt,
    DateTime? CancelledAt
)
{
    public List<OrderItemDto> Items { get; init; } = new();

    // Fulfillment checkpoints (2026-07-04_order_fulfillment.sql). Init-props
    // rather than constructor params so existing SELECTs that don't include
    // the columns keep materializing unchanged; only queries that select the
    // columns populate them (currently the /api/admin/orders endpoints).
    public DateTime? ShippedAt   { get; init; }
    public DateTime? DeliveredAt { get; init; }
    public DateTime? ReturnedAt  { get; init; }
}

/// <summary>
/// Returned by CancelOrderAsync when the cancel actually flipped the row
/// (null when the caller lost the race to the webhook or another canceller).
/// Carries the Square payment-link id so the HANDLER layer can revoke the
/// link best-effort after the DB commit — the repository never talks to
/// Square. RecreatedReservations is informational (how many items went
/// back into the customer's basket).
/// </summary>
public sealed record OrderCancelResult(
    int     OrderId,
    string? SquarePaymentLinkId,
    int     RecreatedReservations);

public record OrderItemDto(
    int     OrderItemId,
    int     OrderId,
    int     ReservationId,
    int     InventoryId,
    string  ItemName,
    string? ItemSku,
    int     UnitPriceCents,
    Guid?   ItemPublicId,
    string? ThumbnailUrl
);

// Checkout submission. The customer ticked some checkboxes in the basket
// (#3 in the design); we send the reservation IDs they want to buy plus
// the address they want it shipped to. The handler revalidates every
// reservation is still Active and owned by the caller before creating
// the Order — see #6 concurrency note.
public record CheckoutRequest(
    List<int> ReservationIds,
    int       AddressId,
    string?   CustomerNotes
);

// ── Message (additions) ───────────────────────────────────────
//
// MessageDto gains an OrderId for order-level auto-messages. The existing
// ReservationId stays — it's how "Ask Noemi about this piece" threads
// (#4) anchor a message to a specific basket item.

public record MessageDto(
    int       MessageId,
    int       CustomerId,
    int?      ReservationId,
    int?      OrderId,
    string    Direction,          // Inbound | Outbound
    string    Body,
    bool      IsRead,
    DateTime  SentAt
);

public record SendMessageRequest(
    string Body,
    int?   ReservationId,         // for "Ask Noemi" per-item threads
    int?   OrderId                // for order-level threads (rarely used by customer)
);

// ── Customer-side "Ask Noemi about this piece" ────────────────
//
// Per-item question button on every basket row. Server creates the
// auto-greeting message ("Question about: {ItemName}") tagged with
// ReservationId so the admin sees what piece the question is about.
// The customer's actual question follows as a normal SendMessageRequest.

public record AskNoemiRequest(
    int     ReservationId,
    string? OpeningQuestion       // optional — sent as a second message if present
);