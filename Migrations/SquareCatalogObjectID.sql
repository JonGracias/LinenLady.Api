-- Adds the column for Square catalog idempotency.
-- Nullable + filtered unique index = unique among non-null values only,
-- so manually-created items (which leave this null) won't collide.

BEGIN TRANSACTION;

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('inv.Inventory')
      AND name = 'SquareCatalogObjectId'
)
BEGIN
    ALTER TABLE inv.Inventory
    ADD SquareCatalogObjectId NVARCHAR(64) NULL;
END;

-- Note: filtered indexes need QUOTED_IDENTIFIER ON, which Azure SQL
-- and modern SSMS/VS Code SQL tools default to. If you hit issues here,
-- prepend the script with: SET QUOTED_IDENTIFIER ON;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UX_Inventory_SquareCatalogObjectId'
      AND object_id = OBJECT_ID('inv.Inventory')
)
BEGIN
    CREATE UNIQUE INDEX UX_Inventory_SquareCatalogObjectId
        ON inv.Inventory(SquareCatalogObjectId)
        WHERE SquareCatalogObjectId IS NOT NULL;
END;

-- Verify
SELECT name, system_type_name = TYPE_NAME(user_type_id), is_nullable, max_length
FROM sys.columns
WHERE object_id = OBJECT_ID('inv.Inventory')
  AND name = 'SquareCatalogObjectId';

-- COMMIT TRANSACTION;
-- ROLLBACK TRANSACTION;