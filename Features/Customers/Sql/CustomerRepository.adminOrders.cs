// Features/Customers/Sql/CustomerRepository.adminOrders.cs
//
// Admin-facing order queries — the back-office /admin/orders pages. Kept in
// their own partial because they cross the customer boundary (they read ALL
// orders, joined to customer identity) and reference the fulfillment
// timestamp columns that customer-facing selects deliberately don't.

namespace LinenLady.API.Customers.Sql;

using Dapper;
using LinenLady.API.Contracts;

public partial interface ICustomerRepository
{
    /// <summary>Every order, newest first, with buyer identity and item count.</summary>
    Task<List<AdminOrderListItem>> GetAllOrdersAdminAsync();

    /// <summary>One order with items, fulfillment timestamps, and buyer identity.</summary>
    Task<AdminOrderDetail?> GetOrderAdminAsync(int orderId);

    /// <summary>
    /// Set (or clear) a fulfillment timestamp. Checkpoint is
    /// "shipped" | "delivered" | "returned"; throws ArgumentException on
    /// anything else. Returns false when the order doesn't exist.
    /// </summary>
    Task<bool> SetOrderCheckpointAsync(int orderId, string checkpoint, bool clear);
}

public partial class CustomerRepository
{
    public async Task<List<AdminOrderListItem>> GetAllOrdersAdminAsync()
    {
        using var db = Connect();

        var rows = await db.QueryAsync<AdminOrderListItem>(
            """
            SELECT
                o.OrderId, o.Status, o.AmountCents,
                (SELECT COUNT(1) FROM cust.OrderItem oi WHERE oi.OrderId = o.OrderId) AS ItemCount,
                LTRIM(RTRIM(CONCAT(ISNULL(c.FirstName, ''), ' ', ISNULL(c.LastName, '')))) AS CustomerName,
                c.Email AS CustomerEmail,
                o.CreatedAt, o.PaidAt, o.CancelledAt,
                o.ShippedAt, o.DeliveredAt, o.ReturnedAt
            FROM cust.[Order]   o
            JOIN cust.Customer  c ON c.CustomerId = o.CustomerId
            ORDER BY o.CreatedAt DESC
            """);

        return rows.ToList();
    }

    public async Task<AdminOrderDetail?> GetOrderAdminAsync(int orderId)
    {
        using var db = Connect();

        var order = await db.QueryFirstOrDefaultAsync<OrderDto>(
            """
            SELECT o.OrderId, o.CustomerId, o.Status, o.AmountCents,
                   o.SquarePaymentLinkUrl, o.SquareOrderId,
                   o.ShipLabel, o.ShipStreet1, o.ShipStreet2,
                   o.ShipCity, o.ShipState, o.ShipZip, o.ShipCountry,
                   o.CustomerNotes, o.CreatedAt, o.PaidAt, o.CancelledAt,
                   o.ShippedAt, o.DeliveredAt, o.ReturnedAt
            FROM cust.[Order] o
            WHERE o.OrderId = @orderId
            """,
            new { orderId });
        if (order is null) return null;

        var items = (await db.QueryAsync<OrderItemDto>(
            OrderItemSelect + " WHERE oi.OrderId = @orderId ORDER BY oi.OrderItemId",
            new { orderId })).ToList();

        var buyer = await db.QueryFirstOrDefaultAsync<(string? FirstName, string? LastName, string? Email)>(
            "SELECT FirstName, LastName, Email FROM cust.Customer WHERE CustomerId = @Id",
            new { Id = order.CustomerId });

        var name = string.Join(" ",
            new[] { buyer.FirstName, buyer.LastName }
                .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = buyer.Email ?? "";

        return new AdminOrderDetail(
            order with { Items = items },
            name,
            buyer.Email ?? "");
    }

    // Whitelist maps checkpoint names to columns — the column name is
    // interpolated into the UPDATE, so it must never come from user input
    // directly.
    private static readonly Dictionary<string, string> CheckpointColumns =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["shipped"]   = "ShippedAt",
            ["delivered"] = "DeliveredAt",
            ["returned"]  = "ReturnedAt",
        };

    public async Task<bool> SetOrderCheckpointAsync(int orderId, string checkpoint, bool clear)
    {
        if (!CheckpointColumns.TryGetValue(checkpoint, out var column))
            throw new ArgumentException(
                $"Unknown checkpoint '{checkpoint}'. Expected shipped | delivered | returned.");

        using var db = Connect();
        var affected = await db.ExecuteAsync(
            $"UPDATE cust.[Order] SET {column} = @Value WHERE OrderId = @OrderId",
            new { Value = clear ? (DateTime?)null : DateTime.UtcNow, OrderId = orderId });

        return affected > 0;
    }
}
