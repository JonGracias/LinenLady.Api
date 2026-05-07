// Features/Customers/Sql/CustomerRepository.basket.cs
//
// Basket + Order repository extensions. Lives alongside the existing
// CustomerRepository — same connection, same Dapper conventions, same DI
// registration. Split into a separate file to keep the basket/order surface
// reviewable without re-reading the 600-line Customer repo.
//
// Once we drop the legacy reservation columns post-launch (PaymentSentAt /
// CompletedAt / SquarePaymentLinkUrl on cust.Reservation) the original
// repo file shrinks substantially and these can fold back in.

namespace LinenLady.API.Customers.Sql;

using System.Data;
using Dapper;
using LinenLady.API.Contracts;
using Microsoft.Data.SqlClient;
using LinenLady.API.Customers.Handler;

// Extends the existing ICustomerRepository surface. The two-status world
// changes a few existing method shapes (CreateReservationAsync no longer
// takes amountCents — the repo looks it up; UpdateReservationStatusAsync
// is gone in favor of explicit ExpireReservationAsync / ReactivateReservationAsync).

public partial interface ICustomerRepository
{
    // ── Basket (reservations as the customer sees them) ──────────────

    /// <summary>
    /// All Active + Expired reservations for a customer, newest first.
    /// "Recently expired" UX (#3) consumes the same list and filters
    /// client-side; the API returns both so the UI can show a unified
    /// timeline without two round-trips.
    /// </summary>
    Task<List<ReservationDto>> GetBasketAsync(int customerId);

    /// <summary>
    /// Adds an item to the customer's basket — creates a new Active
    /// reservation if (a) the inventory item is purchasable and (b) no
    /// other customer holds an Active reservation on it. Returns null
    /// if the item is unavailable; throws ItemAlreadyReservedByYouException
    /// from the handler layer if the *same* customer already holds it.
    /// </summary>
    Task<ReservationDto?> CreateBasketItemAsync(int customerId, AddToBasketRequest req);

    /// <summary>
    /// Marks a single reservation Expired. Used by the "remove from basket"
    /// button (#3) and by the basket-row swipe gesture. Returns the updated
    /// row, or null if the reservation isn't owned by this customer or
    /// already Expired (idempotent — clicking remove twice is fine).
    /// </summary>
    Task<ReservationDto?> RemoveBasketItemAsync(int customerId, int reservationId);

    /// <summary>
    /// Reactivates a previously-expired reservation by inserting a new
    /// Active row for the same inventory item. Used by the "try again"
    /// affordance on recently-expired rows. Subject to the same
    /// availability checks as CreateBasketItemAsync — returns null if
    /// someone else now holds it or if the item is no longer for sale.
    /// </summary>
    Task<ReservationDto?> ReAddBasketItemAsync(int customerId, int reservationId);

    // ── Orders ──────────────────────────────────────────────────────

    /// <summary>
    /// Atomic checkout: validates every reservation in <paramref name="reservationIds"/>
    /// is currently Active and owned by <paramref name="customerId"/>, snapshots
    /// the address, creates the Order row in PaymentPending, creates OrderItem
    /// rows linked to each reservation, and flips those reservations to Expired.
    /// All in one transaction — partial success is impossible by design.
    ///
    /// Returns the newly-created Order with its items hydrated. The amount
    /// total is computed server-side from inventory prices, not trusted from
    /// the request.
    ///
    /// Throws ReservationConflictException if any reservation is no longer
    /// Active or doesn't belong to the customer (#6 race protection).
    /// Throws ArgumentException if reservationIds is empty.
    /// </summary>
    Task<OrderDto> CheckoutAsync(int customerId, CheckoutRequest req);

    Task<OrderDto?>     GetOrderAsync(int orderId);
    Task<OrderDto?>     GetOrderBySquareIdAsync(string squareOrderId);
    Task<List<OrderDto>> GetCustomerOrdersAsync(int customerId);

    /// <summary>Stamp the Square link onto an existing Order (post-create).</summary>
    Task<OrderDto?> SetOrderPaymentLinkAsync(int orderId, SquarePaymentLinkResult link);

    /// <summary>Mark an Order Paid (Square webhook). Also flips inv.Inventory.IsActive=0
    /// on every line item so the piece disappears from /shop. Idempotent —
    /// calling twice is safe.</summary>
    Task<OrderDto?> MarkOrderPaidAsync(string squareOrderId);

    /// <summary>Mark an Order Cancelled or Failed (timeout sweep / manual).
    /// Recreates Active reservations for each item that's still purchasable
    /// (no other customer beat the recovering customer to it in the meantime),
    /// so the items return to the basket. Items that are no longer available
    /// silently drop — the customer sees them as expired in the next basket
    /// load. Returns count of reservations recreated.</summary>
    Task<int> CancelOrderAsync(int orderId, string newStatus); // 'Cancelled' | 'Failed'

    /// <summary>Sweep: orders stuck in PaymentPending past <paramref name="timeoutHours"/>
    /// are cancelled and their items returned to baskets where possible.
    /// Mirrors the existing ExpireReservationsAsync sweeper pattern.</summary>
    Task<int> ExpireStaleOrdersAsync(int timeoutHours);
}

public partial class CustomerRepository
{
    // ── Basket queries ──────────────────────────────────────────────

    private const string BasketSelect = """
        SELECT
            r.ReservationId, r.CustomerId, r.InventoryId, r.Status,
            r.ReservedAt, r.ExpiresAt, r.CustomerNotes,
            i.Name           AS ItemName,
            i.Sku            AS ItemSku,
            i.PublicId       AS ItemPublicId,
            CAST(NULL AS NVARCHAR(2048)) AS ThumbnailUrl,
            i.UnitPriceCents,
            -- CanReAdd: true only for Expired rows where the item is still
            -- purchasable AND nobody else holds an active reservation on it.
            -- Uses NOT EXISTS rather than a LEFT JOIN so the planner doesn't
            -- have to deduplicate on the customer's own newer rows.
            CAST(
                CASE
                    WHEN r.Status = 'Expired'
                     AND i.IsActive = 1 AND i.IsDraft = 0 AND i.IsDeleted = 0
                     AND NOT EXISTS (
                         SELECT 1 FROM cust.Reservation r2
                         WHERE  r2.InventoryId = r.InventoryId
                           AND  r2.Status      = 'Active'
                           AND  r2.ExpiresAt   > SYSUTCDATETIME()
                     )
                    THEN 1 ELSE 0
                END AS BIT) AS CanReAdd
        FROM   cust.Reservation r
        JOIN   inv.Inventory    i ON i.InventoryId = r.InventoryId
        """;

    public async Task<List<ReservationDto>> GetBasketAsync(int customerId)
    {
        using var db = Connect();
        var rows = await db.QueryAsync<ReservationDto>(
            BasketSelect + """
             WHERE r.CustomerId = @CustomerId
             ORDER BY r.ReservedAt DESC
            """,
            new { CustomerId = customerId });
        return rows.ToList();
    }

    public async Task<ReservationDto?> CreateBasketItemAsync(
        int customerId, AddToBasketRequest req)
    {
        using var db = Connect();

        // Single round-trip: the INSERT is conditional on availability so
        // we don't have a TOCTOU window between "is it available?" and
        // "claim it." The race-loser gets a no-op; the handler layer
        // surfaces the right 409 by re-querying.
        return await db.QueryFirstOrDefaultAsync<ReservationDto>(
            $"""
            DECLARE @Created TABLE (ReservationId INT);

            INSERT INTO cust.Reservation
                (CustomerId, InventoryId, Status, ExpiresAt, CustomerNotes, AmountCents)
            OUTPUT inserted.ReservationId INTO @Created
            SELECT @CustomerId, @InventoryId, 'Active',
                   DATEADD(DAY, 2, SYSUTCDATETIME()), @CustomerNotes, i.UnitPriceCents
            FROM   inv.Inventory i
            WHERE  i.InventoryId = @InventoryId
              AND  i.IsActive = 1 AND i.IsDraft = 0 AND i.IsDeleted = 0
              AND  NOT EXISTS (
                       SELECT 1 FROM cust.Reservation r
                       WHERE  r.InventoryId = i.InventoryId
                         AND  r.Status      = 'Active'
                         AND  r.ExpiresAt   > SYSUTCDATETIME()
                   );

            IF NOT EXISTS (SELECT 1 FROM @Created)
                SELECT TOP 0 NULL;  -- handler maps null → NotFound or Conflict
            ELSE
                {BasketSelect}
                WHERE r.ReservationId = (SELECT TOP 1 ReservationId FROM @Created);
            """,
            new { CustomerId = customerId, req.InventoryId, req.CustomerNotes });
    }

    public async Task<ReservationDto?> RemoveBasketItemAsync(
        int customerId, int reservationId)
    {
        using var db = Connect();
        return await db.QueryFirstOrDefaultAsync<ReservationDto>(
            $"""
            UPDATE cust.Reservation
            SET    Status    = 'Expired',
                   UpdatedAt = SYSUTCDATETIME()
            WHERE  ReservationId = @ReservationId
              AND  CustomerId    = @CustomerId
              AND  Status        = 'Active';

            {BasketSelect}
            WHERE r.ReservationId = @ReservationId
              AND r.CustomerId    = @CustomerId;
            """,
            new { ReservationId = reservationId, CustomerId = customerId });
    }

    public async Task<ReservationDto?> ReAddBasketItemAsync(
        int customerId, int reservationId)
    {
        using var db = Connect();

        // Look up the inventory id from the expired row, then go through
        // the normal create path. Same availability checks apply — this is
        // intentionally not a "shortcut" past the race protection.
        var inventoryId = await db.ExecuteScalarAsync<int?>(
            """
            SELECT InventoryId FROM cust.Reservation
            WHERE  ReservationId = @ReservationId
              AND  CustomerId    = @CustomerId
              AND  Status        = 'Expired'
            """,
            new { ReservationId = reservationId, CustomerId = customerId });

        if (inventoryId is null) return null;

        return await CreateBasketItemAsync(
            customerId,
            new AddToBasketRequest(inventoryId.Value, CustomerNotes: null));
    }

    // ── Order queries ──────────────────────────────────────────────

    private const string OrderSelect = """
        SELECT
            o.OrderId, o.CustomerId, o.Status, o.AmountCents,
            o.SquarePaymentLinkUrl, o.SquareOrderId,
            o.ShipLabel, o.ShipStreet1, o.ShipStreet2,
            o.ShipCity, o.ShipState, o.ShipZip, o.ShipCountry,
            o.CustomerNotes, o.CreatedAt, o.PaidAt, o.CancelledAt
        FROM cust.[Order] o
        """;

    private const string OrderItemSelect = """
        SELECT
            oi.OrderItemId, oi.OrderId, oi.ReservationId, oi.InventoryId,
            oi.ItemName, oi.ItemSku, oi.UnitPriceCents,
            i.PublicId AS ItemPublicId,
            CAST(NULL AS NVARCHAR(2048)) AS ThumbnailUrl
        FROM   cust.OrderItem oi
        JOIN   inv.Inventory  i ON i.InventoryId = oi.InventoryId
        """;

    public async Task<OrderDto?> GetOrderAsync(int orderId)
    {
        using var db = Connect();
        return await HydrateOrderAsync(db, "WHERE o.OrderId = @OrderId", new { OrderId = orderId });
    }

    public async Task<OrderDto?> GetOrderBySquareIdAsync(string squareOrderId)
    {
        using var db = Connect();
        return await HydrateOrderAsync(
            db, "WHERE o.SquareOrderId = @SquareOrderId",
            new { SquareOrderId = squareOrderId });
    }

    public async Task<List<OrderDto>> GetCustomerOrdersAsync(int customerId)
    {
        using var db = Connect();

        // 1. Fetch the order rows. 17 columns, in constructor order.
        //    SquarePaymentLinkId on the table is intentionally not selected —
        //    OrderDto only exposes the URL.
        var orders = (await db.QueryAsync<OrderDto>(
            """
            SELECT OrderId, CustomerId, Status, AmountCents,
                SquarePaymentLinkUrl, SquareOrderId,
                ShipLabel, ShipStreet1, ShipStreet2, ShipCity, ShipState, ShipZip, ShipCountry,
                CustomerNotes, CreatedAt, PaidAt, CancelledAt
            FROM cust.[Order]
            WHERE CustomerId = @customerId
            ORDER BY CreatedAt DESC
            """,
            new { customerId })).ToList();

        if (orders.Count == 0) return orders;

        // 2. Fetch items for those orders in one round-trip.
        //    Dapper expands @orderIds into (?, ?, ?...) for an IN clause.
        var orderIds = orders.Select(o => o.OrderId).ToArray();

        var items = (await db.QueryAsync<OrderItemDto>(
            OrderItemSelect + " WHERE oi.OrderId IN @orderIds ORDER BY oi.OrderId, oi.OrderItemId",
            new { orderIds })).ToList();

        // 3. Group items by OrderId and attach. `with` works on records;
        //    the resulting list preserves the CreatedAt DESC ordering from
        //    the orders query.
        var byOrderId = items.GroupBy(i => i.OrderId)
                            .ToDictionary(g => g.Key, g => g.ToList());

        return orders
            .Select(o => o with { Items = byOrderId.GetValueOrDefault(o.OrderId, new List<OrderItemDto>()) })
            .ToList();
    }

    private async Task<OrderDto?> HydrateOrderAsync(
        IDbConnection db, string whereClause, object parameters)
    {
        var order = await db.QueryFirstOrDefaultAsync<OrderDto>(
            OrderSelect + " " + whereClause, parameters);
        if (order is null) return null;

        var items = (await db.QueryAsync<OrderItemDto>(
            OrderItemSelect + " WHERE oi.OrderId = @OrderId",
            new { order.OrderId })).ToList();

        return order with { Items = items };
    }

    // ── Checkout (the transactional heart) ──────────────────────────

    public async Task<OrderDto> CheckoutAsync(int customerId, CheckoutRequest req)
    {
        if (req.ReservationIds is null || req.ReservationIds.Count == 0)
            throw new ArgumentException("At least one reservation must be selected.");

        using var conn = (SqlConnection)Connect();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction(IsolationLevel.Serializable);

        try
        {
            // 1. Pull the customer's address by id (snapshot source).
            var address = await conn.QueryFirstOrDefaultAsync<CustomerAddressDto>(
                """
                SELECT AddressId, CustomerId, Label, Street1, Street2,
                       City, State, Zip, Country, IsDefault
                FROM   cust.CustomerAddress
                WHERE  AddressId  = @AddressId
                  AND  CustomerId = @CustomerId
                """,
                new { req.AddressId, CustomerId = customerId },
                tx);

            if (address is null)
                throw new ArgumentException(
                    "Address not found or doesn't belong to this customer.");

            // 2. Lock + validate every selected reservation in one shot.
            // Serializable + this UPDLOCK/HOLDLOCK select prevents the
            // expiry sweeper from flipping a row between our check and
            // our insert. Returning fewer rows than requested means the
            // customer included a stale id — we surface that explicitly.
            var rows = (await conn.QueryAsync<(int ReservationId, int InventoryId,
                                               int UnitPriceCents, string ItemName, string? ItemSku)>(
                """
                SELECT r.ReservationId, r.InventoryId, i.UnitPriceCents,
                       i.Name AS ItemName, i.Sku AS ItemSku
                FROM   cust.Reservation r WITH (UPDLOCK, HOLDLOCK)
                JOIN   inv.Inventory    i ON i.InventoryId = r.InventoryId
                WHERE  r.ReservationId IN @ReservationIds
                  AND  r.CustomerId     = @CustomerId
                  AND  r.Status         = 'Active'
                  AND  r.ExpiresAt      > SYSUTCDATETIME()
                """,
                new { req.ReservationIds, CustomerId = customerId },
                tx)).ToList();

            if (rows.Count != req.ReservationIds.Count)
            {
                var found   = rows.Select(r => r.ReservationId).ToHashSet();
                var missing = req.ReservationIds.Where(id => !found.Contains(id)).ToList();
                throw new ReservationConflictException(
                    $"One or more items in your basket have expired or are no longer available: " +
                    $"reservation ids [{string.Join(", ", missing)}]. Refresh your basket and try again.");
            }

            var amountCents = rows.Sum(r => r.UnitPriceCents);

            // 3. Insert the Order with the address snapshot.
            var orderId = await conn.ExecuteScalarAsync<int>(
                """
                INSERT INTO cust.[Order]
                    (CustomerId, Status, AmountCents,
                     ShipLabel, ShipStreet1, ShipStreet2, ShipCity, ShipState, ShipZip, ShipCountry,
                     CustomerNotes)
                OUTPUT inserted.OrderId
                VALUES
                    (@CustomerId, 'PaymentPending', @AmountCents,
                     @Label, @Street1, @Street2, @City, @State, @Zip, @Country,
                     @CustomerNotes);
                """,
                new
                {
                    CustomerId  = customerId,
                    AmountCents = amountCents,
                    address.Label, address.Street1, address.Street2,
                    address.City,  address.State,   address.Zip, address.Country,
                    req.CustomerNotes
                }, tx);

            // 4. Insert OrderItem rows + flip reservations to Expired.
            // Done as a single TVP-style INSERT for cleanliness; with a few
            // dozen items max per order, the loop overhead is negligible.
            foreach (var r in rows)
            {
                await conn.ExecuteAsync(
                    """
                    INSERT INTO cust.OrderItem
                        (OrderId, ReservationId, InventoryId,
                         ItemName, ItemSku, UnitPriceCents)
                    VALUES (@OrderId, @ReservationId, @InventoryId,
                            @ItemName, @ItemSku, @UnitPriceCents);

                    UPDATE cust.Reservation
                    SET    Status    = 'Expired',
                           UpdatedAt = SYSUTCDATETIME()
                    WHERE  ReservationId = @ReservationId;
                    """,
                    new
                    {
                        OrderId = orderId,
                        r.ReservationId, r.InventoryId,
                        r.ItemName, r.ItemSku, r.UnitPriceCents
                    }, tx);
            }

            tx.Commit();

            return await GetOrderAsync(orderId)
                ?? throw new InvalidOperationException("Order created but couldn't be re-read.");
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<OrderDto?> SetOrderPaymentLinkAsync(
        int orderId, SquarePaymentLinkResult link)
    {
        using var db = Connect();
        await db.ExecuteAsync(
            """
            UPDATE cust.[Order]
            SET    SquarePaymentLinkId  = @PaymentLinkId,
                   SquarePaymentLinkUrl = @Url,
                   SquareOrderId        = @OrderId
            WHERE  OrderId = @OrderRowId;
            """,
            new { OrderRowId = orderId, link.PaymentLinkId, link.Url, link.OrderId });
        return await GetOrderAsync(orderId);
    }

    public async Task<OrderDto?> MarkOrderPaidAsync(string squareOrderId)
    {
        using var conn = (SqlConnection)Connect();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        try
        {
            // Idempotent: only flip if currently PaymentPending. Square may
            // retry-deliver the webhook; subsequent calls become no-ops.
            var orderId = await conn.ExecuteScalarAsync<int?>(
                """
                UPDATE cust.[Order]
                SET    Status = 'Paid',
                       PaidAt = SYSUTCDATETIME()
                OUTPUT inserted.OrderId
                WHERE  SquareOrderId = @SquareOrderId
                  AND  Status        = 'PaymentPending';
                """,
                new { SquareOrderId = squareOrderId }, tx);

            if (orderId is null)
            {
                tx.Commit();
                // Could be: order already Paid (idempotent retry), order doesn't
                // exist (Square sent a webhook for something we don't track), or
                // order already Cancelled (paid AFTER timeout — edge case). Let
                // the caller decide what to do; we just return whatever's there.
                return await GetOrderBySquareIdAsync(squareOrderId);
            }

            // Flip every line item's inventory row to inactive so it
            // disappears from /shop. Uses the existing IsActive convention —
            // no new "sold" status needed.
            await conn.ExecuteAsync(
                """
                UPDATE i
                SET    i.IsActive  = 0,
                       i.UpdatedAt = SYSUTCDATETIME()
                FROM   inv.Inventory  i
                JOIN   cust.OrderItem oi ON oi.InventoryId = i.InventoryId
                WHERE  oi.OrderId = @OrderId;
                """,
                new { OrderId = orderId.Value }, tx);

            tx.Commit();
            return await GetOrderAsync(orderId.Value);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<int> CancelOrderAsync(int orderId, string newStatus)
    {
        if (newStatus != "Cancelled" && newStatus != "Failed")
            throw new ArgumentException($"Invalid order status '{newStatus}'.");

        using var conn = (SqlConnection)Connect();
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        try
        {
            // Flip the order. If it's not in PaymentPending we don't touch it
            // (already Paid or already Cancelled — idempotent).
            var affected = await conn.ExecuteAsync(
                """
                UPDATE cust.[Order]
                SET    Status      = @NewStatus,
                       CancelledAt = SYSUTCDATETIME()
                WHERE  OrderId     = @OrderId
                  AND  Status      = 'PaymentPending';
                """,
                new { OrderId = orderId, NewStatus = newStatus }, tx);

            if (affected == 0)
            {
                tx.Commit();
                return 0;
            }

            // Recreate Active reservations for items that are still purchasable
            // — INSERT-FROM-SELECT with the same availability filter as
            // CreateBasketItemAsync. Items that someone else has already
            // grabbed silently drop; the customer sees an empty basket
            // for those slots. The tradeoff was discussed in #6 — the
            // alternative (preserve them as "expired with try-again") would
            // double-count the item if the rival's reservation later expires.
            var recreated = await conn.ExecuteAsync(
                """
                INSERT INTO cust.Reservation
                    (CustomerId, InventoryId, Status, ExpiresAt, CustomerNotes)
                SELECT o.CustomerId, oi.InventoryId, 'Active',
                       DATEADD(DAY, 2, SYSUTCDATETIME()), NULL
                FROM   cust.OrderItem  oi
                JOIN   cust.[Order]    o  ON o.OrderId = oi.OrderId
                JOIN   inv.Inventory   i  ON i.InventoryId = oi.InventoryId
                WHERE  oi.OrderId = @OrderId
                  AND  i.IsActive = 1 AND i.IsDraft = 0 AND i.IsDeleted = 0
                  AND  NOT EXISTS (
                           SELECT 1 FROM cust.Reservation r
                           WHERE  r.InventoryId = oi.InventoryId
                             AND  r.Status      = 'Active'
                             AND  r.ExpiresAt   > SYSUTCDATETIME()
                       );
                """,
                new { OrderId = orderId }, tx);

            tx.Commit();
            return recreated;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task<int> ExpireStaleOrdersAsync(int timeoutHours)
    {
        using var db = Connect();

        // Pull stale order ids first, then call CancelOrderAsync per id so
        // the recreation logic is one path. A sweeper that does this all in
        // one MERGE would be faster but harder to keep correct; this
        // happens infrequently (every few minutes at worst) so clarity wins.
        var staleIds = (await db.QueryAsync<int>(
            """
            SELECT OrderId FROM cust.[Order]
            WHERE  Status    = 'PaymentPending'
              AND  CreatedAt < DATEADD(HOUR, -@TimeoutHours, SYSUTCDATETIME())
            """,
            new { TimeoutHours = timeoutHours })).ToList();

        var count = 0;
        foreach (var id in staleIds)
        {
            await CancelOrderAsync(id, "Cancelled");
            count++;
        }
        return count;
    }
}