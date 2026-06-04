SELECT InventoryId, Sku, Name, IsActive, IsDeleted, IsDraft
FROM inv.Inventory
WHERE IsActive = 0;

UPDATE inv.Inventory
SET IsActive = 1,
    UpdatedAt = SYSUTCDATETIME()
WHERE IsActive = 0
  AND IsDeleted = 0;