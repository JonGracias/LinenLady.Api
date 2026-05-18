-- =====================================================================
-- Migration: inv.GetItemAvailability + supporting index
--
-- Adds a scalar table-valued function that returns the "blocking" reason
-- (if any) preventing a new basket-add for a given InventoryId. Returns
-- empty if the item is purchasable.
--
-- Status enum returned in the BlockingState column:
--   'InBasket'       - someone has an Active reservation, not yet expired
--   'PendingPayment' - reservation is in PaymentSent, or Order is PaymentPending
--   'Sold'           - reservation is Completed, or Order is Paid
--   'Inactive'       - inv.Inventory flag check failed (deleted/draft/inactive)
--
-- The BlockingReservationId and BlockingOrderId columns identify the
-- specific row creating the block, useful for support diagnostics and
-- for the "you already have this" case (caller compares CustomerId).
--
-- Apply against: linenlady (dev) AND linenlady_test before running tests.
-- =====================================================================

-- Shared user-defined table type for passing id batches as TVPs.
-- Kept in dbo because it's a generic helper, not feature-specific.
IF TYPE_ID(N'dbo.IntList') IS NULL
BEGIN
    CREATE TYPE dbo.IntList AS TABLE (Id INT NOT NULL PRIMARY KEY);
END
GO

IF OBJECT_ID('inv.GetItemAvailability', 'IF') IS NOT NULL
    DROP FUNCTION inv.GetItemAvailability;
GO

CREATE FUNCTION inv.GetItemAvailability (@InventoryId INT)
RETURNS TABLE
AS
RETURN
    -- Inventory flag check (hard block — item shouldn't even be listed).
    SELECT TOP 1
        CAST('Inactive' AS NVARCHAR(20)) AS BlockingState,
        CAST(NULL       AS INT)          AS BlockingReservationId,
        CAST(NULL       AS INT)          AS BlockingOrderId,
        CAST(NULL       AS INT)          AS BlockingCustomerId
    FROM   inv.Inventory
    WHERE  InventoryId = @InventoryId
      AND  (IsActive = 0 OR IsDraft = 1 OR IsDeleted = 1)

    UNION ALL

    -- Sold: terminal state, never returns to circulation.
    SELECT TOP 1
        'Sold', r.ReservationId, NULL, r.CustomerId
    FROM   cust.Reservation r
    WHERE  r.InventoryId = @InventoryId
      AND  r.Status      = 'Completed'

    UNION ALL

    -- Pending payment: order is mid-Square-checkout. Resolves to Paid (Sold)
    -- or Cancelled/Failed (sweeper releases) in a bounded window.
    SELECT TOP 1
        'PendingPayment', NULL, o.OrderId, o.CustomerId
    FROM   cust.[Order]   o
    JOIN   cust.OrderItem oi ON oi.OrderId = o.OrderId
    WHERE  oi.InventoryId = @InventoryId
      AND  o.Status       = 'PaymentPending'

    UNION ALL

    -- Pending payment (reservation-side) — covers any reservation that
    -- flipped to PaymentSent before the Order row was written, or any
    -- legacy reservation predating cust.Order.
    SELECT TOP 1
        'PendingPayment', r.ReservationId, NULL, r.CustomerId
    FROM   cust.Reservation r
    WHERE  r.InventoryId = @InventoryId
      AND  r.Status      = 'PaymentSent'

    UNION ALL

    -- Live basket hold by someone (could be the caller themselves).
    SELECT TOP 1
        'InBasket', r.ReservationId, NULL, r.CustomerId
    FROM   cust.Reservation r
    WHERE  r.InventoryId = @InventoryId
      AND  r.Status      = 'Active'
      AND  r.ExpiresAt   > SYSUTCDATETIME();
GO

-- The function is a UNION ALL of small filtered scans; each branch needs
-- an index that lets it terminate at TOP 1 cheaply. The existing
-- IX_Reservation_InventoryId_Status covers the reservation branches.
-- Add a matching index on cust.OrderItem for the Order branch.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_OrderItem_InventoryId' AND object_id = OBJECT_ID('cust.OrderItem'))
BEGIN
    CREATE INDEX IX_OrderItem_InventoryId
        ON cust.OrderItem (InventoryId)
        INCLUDE (OrderId);
END
GO
