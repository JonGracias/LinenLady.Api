namespace LinenLady.API.Contracts;

public sealed class UpdateItemRequest
{
    // Direct field updates — null means "don't change"
    public string? Name { get; set; }
    public string? Description { get; set; }
    public int? UnitPriceCents { get; set; }
    public int? QuantityOnHand { get; set; }
    public int? PrimaryImageId { get; set; }

    // State flags
    public bool? IsActive { get; set; }
    public bool? IsFeatured { get; set; }

    // AI rewrite
    public AiRewriteRequest? Ai { get; set; }
}

public sealed class AiRewriteRequest
{
    public string? Hint { get; set; }
    public List<string> Fields { get; set; } = new(); // "name", "description", "price"
}

/// <summary>
/// Resolved field values the repository persists. The handler collapses
/// <see cref="UpdateItemRequest"/> + current state into this shape before SQL.
/// </summary>
public sealed class UpdateItemFields
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int UnitPriceCents { get; set; }
    public int QuantityOnHand { get; set; }
    public bool IsActive { get; set; }
    public bool IsDraft { get; set; }
    public bool IsFeatured { get; set; }
}

public sealed class UpdateItemResponse
{
    public int InventoryId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public int UnitPriceCents { get; set; }
    public int QuantityOnHand { get; set; }
    public bool IsActive { get; set; }
    public bool IsDraft { get; set; }
    public bool IsFeatured { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public enum UpdateItemResult
{
    Updated,
    NotFound,
    BadRequest,
    Failed
}
