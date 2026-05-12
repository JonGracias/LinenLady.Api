namespace LinenLady.API.Inventory.Sql;

using Dapper;
using LinenLady.API.Contracts;
using Microsoft.Data.SqlClient;

// Partial extension to InventoryRepository providing the availability
// lookup used by the storefront. Kept in its own file because it joins
// against cust.* — availability is a customer-flow concern that happens
// to be exposed through the inventory feature for caller convenience.
public sealed partial class InventoryRepository
{
    /// <summary>
    /// Returns the blocking state for each inventory id that is NOT
    /// currently purchasable. Items not present in the result are
    /// implicitly available. Personalization (YourBasket / YourPendingPayment)
    /// happens in the handler, not here — this layer returns raw state.
    /// </summary>
    public async Task<List<ItemAvailabilityDto>> GetAvailability(
        int[] inventoryIds,
        CancellationToken ct)
    {
        if (inventoryIds is null || inventoryIds.Length == 0)
            return new List<ItemAvailabilityDto>();

        // Cap the batch size so a malicious caller can't ask about the
        // whole catalog in one shot. The storefront pages at 24/48 items;
        // 200 is comfortable headroom.
        if (inventoryIds.Length > 200)
            throw new ArgumentException(
                "Availability batch size capped at 200 ids.", nameof(inventoryIds));

        const string sql = """
            SELECT
                i.Id              AS InventoryId,
                a.BlockingState,
                a.BlockingReservationId,
                a.BlockingOrderId,
                a.BlockingCustomerId
            FROM   @Ids i
            CROSS APPLY inv.GetItemAvailability(i.Id) a;
            """;

        using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync(ct);

        var idTable = new System.Data.DataTable();
        idTable.Columns.Add("Id", typeof(int));
        foreach (var id in inventoryIds.Distinct())
            idTable.Rows.Add(id);

        var rows = await conn.QueryAsync<AvailabilityRow>(
            sql,
            new { Ids = idTable.AsTableValuedParameter("dbo.IntList") });

        return rows.Select(r => new ItemAvailabilityDto
        {
            InventoryId        = r.InventoryId,
            State              = r.BlockingState,
            BlockingCustomerId = r.BlockingCustomerId,
        }).ToList();
    }

    private sealed record AvailabilityRow(
        int     InventoryId,
        string  BlockingState,
        int?    BlockingReservationId,
        int?    BlockingOrderId,
        int?    BlockingCustomerId);
}