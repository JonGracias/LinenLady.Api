namespace LinenLady.API.Contracts;

public sealed class InventoryItemDto
{
    public int      InventoryId    { get; set; }
    public Guid?     PublicId       { get; set; }
    public string   Sku            { get; set; } = "";
    public string   Name           { get; set; } = "";
    public string?  Description    { get; set; }
    public int      QuantityOnHand { get; set; }
    public int      UnitPriceCents { get; set; }
    public bool     IsActive       { get; set; }
    public bool     IsDraft        { get; set; }
    public bool     IsDeleted      { get; set; }
    public bool     IsFeatured     { get; set; }   // was missing from your current file
    public DateTime CreatedAt      { get; set; }
    public DateTime UpdatedAt      { get; set; }
    public string?  KeywordsJson   { get; set; }   // new — from inv.InventoryAiMeta

    public List<InventoryImageDto> Images { get; } = new();
}