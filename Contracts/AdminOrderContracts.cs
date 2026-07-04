namespace LinenLady.API.Contracts;

// ── Admin orders (back-office) ─────────────────────────────────────────
//
// Fulfillment model: an order's checkpoint is derived from which
// timestamps are set, in precedence order Returned > Delivered > Shipped >
// Received (PaidAt). The timestamps live on cust.[Order]
// (2026-07-04_order_fulfillment.sql); payment Status is a separate axis.

/// <summary>One row of GET /api/admin/orders.</summary>
public sealed record AdminOrderListItem(
    int       OrderId,
    string    Status,          // PaymentPending | Paid | Cancelled | Failed
    int       AmountCents,
    int       ItemCount,
    string    CustomerName,
    string    CustomerEmail,
    DateTime  CreatedAt,
    DateTime? PaidAt,
    DateTime? CancelledAt,
    DateTime? ShippedAt,
    DateTime? DeliveredAt,
    DateTime? ReturnedAt);

/// <summary>GET /api/admin/orders/{id} — full order plus buyer identity.</summary>
public sealed record AdminOrderDetail(
    OrderDto Order,
    string   CustomerName,
    string   CustomerEmail);

/// <summary>
/// POST /api/admin/orders/{id}/checkpoint body. Checkpoint is one of
/// "shipped" | "delivered" | "returned" (case-insensitive). Clear=true
/// un-sets the timestamp (undo).
/// </summary>
public sealed class SetOrderCheckpointRequest
{
    public string Checkpoint { get; set; } = "";
    public bool   Clear      { get; set; }
}
