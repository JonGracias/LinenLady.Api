namespace LinenLady.API.Inventory.Items.Handler;

using LinenLady.API.Contracts;
using LinenLady.API.Inventory.Sql;

public sealed class GetItemsHandler
{
    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "all", "draft", "active", "featured"
    };

    private readonly IInventoryRepository _repo;

    public GetItemsHandler(IInventoryRepository repo)
    {
        _repo = repo;
    }

    public async Task<(GetItemsResult Result, GetItemsResponse? Response)> Handle(
        GetItemsQuery query,
        CancellationToken ct)
    {
        var status = (query.Status ?? "all").Trim().ToLowerInvariant();
        if (!ValidStatuses.Contains(status))
            return (GetItemsResult.BadRequest, null);

        query.Status = status;

        var (items, totalCount) = await _repo.GetItems(query, ct);

        var limit = query.Limit; // repo clamped
        var page = query.Page;   // repo clamped
        var totalPages = (int)Math.Max(1, (totalCount + limit - 1) / limit);

        return (GetItemsResult.Ok, new GetItemsResponse
        {
            Items      = items,
            Page       = page,
            Limit      = limit,
            TotalCount = totalCount,
            TotalPages = totalPages,
            Status     = status,
            Category   = query.Category?.Trim().ToLowerInvariant() ?? ""
        });
    }
}

public enum GetItemsResult
{
    Ok,
    BadRequest
}
