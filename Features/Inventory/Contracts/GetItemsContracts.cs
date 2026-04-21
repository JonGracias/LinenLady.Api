namespace LinenLady.API.Contracts;

/// <summary>
/// Discriminator for looking up an item by either numeric id or SKU.
/// Use the static factories: ItemKey.ById(42) or ItemKey.BySku("abc").
/// </summary>
public readonly record struct ItemKey
{
    public int? Id { get; init; }
    public string? Sku { get; init; }

    public static ItemKey ById(int id) => new() { Id = id };
    public static ItemKey BySku(string sku) => new() { Sku = sku };

    public bool IsId => Id.HasValue;
    public bool IsSku => !string.IsNullOrWhiteSpace(Sku);
}

public sealed class GetItemsQuery
{
    public int Page { get; set; } = 1;
    public int Limit { get; set; } = 10;
    public string Status { get; set; } = "all"; // all | draft | active | featured
    public string? Category { get; set; }
}

public sealed class GetItemsResponse
{
    public List<InventoryItemDto> Items { get; set; } = new();
    public int Page { get; set; }
    public int Limit { get; set; }
    public long TotalCount { get; set; }
    public int TotalPages { get; set; }
    public string Status { get; set; } = "all";
    public string Category { get; set; } = "";
}

public sealed class GetCountsResponse
{
    public long All { get; set; }
    public long Drafts { get; set; }
    public long Published { get; set; }
}
