-- See what's holding it
SELECT r.ReservationId, r.CustomerId, r.Status, r.ExpiresAt, r.ReservedAt
FROM cust.Reservation r
WHERE r.InventoryId = 13084
  AND r.Status = 'Active'
  AND r.ExpiresAt > SYSUTCDATETIME();

-- Once confirmed, expire it manually
UPDATE cust.Reservation
SET    Status    = 'Expired',
       UpdatedAt = SYSUTCDATETIME()
WHERE  InventoryId = 13084
  AND  Status      = 'Active';