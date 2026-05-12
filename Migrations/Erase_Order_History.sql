USE linenlady;
GO

IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
GO

BEGIN TRANSACTION;

-- 1. Re-activate inventory for items that were sold.
UPDATE i
SET    i.IsActive  = 1,
       i.UpdatedAt = SYSUTCDATETIME()
FROM   inv.Inventory  i
JOIN   cust.OrderItem oi ON oi.InventoryId = i.InventoryId
JOIN   cust.[Order]   o  ON o.OrderId       = oi.OrderId
WHERE  o.Status = 'Paid'
  AND  i.IsActive = 0;

-- 2. Wipe in FK-safe order:
--    Message       -> Order, Reservation
--    Notification  -> Reservation
--    OrderItem     -> Order, Reservation
--    then Order and Reservation are free.
DELETE FROM cust.Message;
DELETE FROM cust.Notification;
DELETE FROM cust.OrderItem;
DELETE FROM cust.[Order];
DELETE FROM cust.Reservation;

-- 3. Reset identity counters.
DBCC CHECKIDENT ('cust.Message',      RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('cust.Notification', RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('cust.OrderItem',    RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('cust.[Order]',      RESEED, 0) WITH NO_INFOMSGS;
DBCC CHECKIDENT ('cust.Reservation',  RESEED, 0) WITH NO_INFOMSGS;

-- 4. Verify.
SELECT 'Messages'           AS TableName, COUNT(*) AS NumRows FROM cust.Message
UNION ALL
SELECT 'Notifications',                   COUNT(*)            FROM cust.Notification
UNION ALL
SELECT 'Orders',                          COUNT(*)            FROM cust.[Order]
UNION ALL
SELECT 'OrderItems',                      COUNT(*)            FROM cust.OrderItem
UNION ALL
SELECT 'Reservations',                    COUNT(*)            FROM cust.Reservation
UNION ALL
SELECT 'Inactive Inventory (excl draft/deleted)',
       COUNT(*)             FROM inv.Inventory
       WHERE IsActive = 0 AND IsDraft = 0 AND IsDeleted = 0;

COMMIT TRANSACTION;
-- Or: ROLLBACK TRANSACTION;