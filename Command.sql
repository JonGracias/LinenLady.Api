-- Step 1: Drop all foreign keys
DECLARE @sql NVARCHAR(MAX) = '';
SELECT @sql += 'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(parent_object_id)) + '.' 
            + QUOTENAME(OBJECT_NAME(parent_object_id))
            + ' DROP CONSTRAINT ' + QUOTENAME(name) + ';' + CHAR(10)
FROM sys.foreign_keys;
EXEC sp_executesql @sql;

-- Step 2: Drop all tables
SET @sql = '';
SELECT @sql += 'DROP TABLE ' + QUOTENAME(SCHEMA_NAME(schema_id)) + '.' + QUOTENAME(name) + ';' + CHAR(10)
FROM sys.tables;
EXEC sp_executesql @sql;

-- Step 3: Verify (should return zero rows)
SELECT name FROM sys.tables;