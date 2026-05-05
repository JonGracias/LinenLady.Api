-- ============================================================================
-- 2026-05-04 basket + orders model
-- ============================================================================
-- Reshapes the reservation/checkout flow:
--   * cust.Reservation collapses to two statuses: Active | Expired.
--     (Pending/Confirmed/PaymentSent/Completed/Cancelled go away — the
--     concepts they carried move to cust.Order.)
--   * Hold window extends from 48h to 2 days = same number, but the field is
--     now the only thing controlling basket-removal timing, so we re-state it
--     explicitly with a default.
--   * New cust.Order table — the post-checkout artifact, snapshotted with
--     shipping address so historical orders never retroactively change when
--     the customer edits their saved address.
--   * New cust.OrderItem — line items linking an Order to the Reservations
--     it consumed. We keep the Reservation rows around (rather than deleting
--     them) so the audit trail survives.
--   * cust.Message gets an OrderId column alongside its existing ReservationId
--     so the auto-message-on-order-placed flow has somewhere to point. The
--     existing ReservationId column stays for the per-item "Ask Noemi"
--     threads.
--
-- Run order matters — the Reservation status backfill below assumes the new
-- columns/constraints don't exist yet at the point we run the UPDATE.
-- Idempotent — safe to re-run.
-- ============================================================================

SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;

-- ── 1. cust.Order ───────────────────────────────────────────────────────────
-- Address fields are SNAPSHOTTED at checkout time. If the customer edits their
-- saved address afterward, the Order row keeps the address it shipped to.
IF OBJECT_ID('cust.[Order]', 'U') IS NULL
BEGIN
    CREATE TABLE cust.[Order] (
        OrderId             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Order PRIMARY KEY,
        CustomerId          INT NOT NULL
            CONSTRAINT FK_Order_Customer REFERENCES cust.Customer(CustomerId),
        Status              NVARCHAR(20) NOT NULL
            CONSTRAINT CK_Order_Status CHECK (Status IN ('PaymentPending','Paid','Cancelled','Failed')),

        -- Money — denormalized total at checkout time. Sum-of-items at the
        -- moment of submission; we don't recompute it later because prices on
        -- the inventory rows could change.
        AmountCents         INT NOT NULL CONSTRAINT CK_Order_Amount CHECK (AmountCents >= 0),

        -- Square integration
        SquareOrderId           NVARCHAR(64)  NULL,
        SquarePaymentLinkId     NVARCHAR(64)  NULL,
        SquarePaymentLinkUrl    NVARCHAR(500) NULL,

        -- Address snapshot (mirrors cust.CustomerAddress columns exactly).
        -- Nullable because a Square checkout *can* succeed with the customer
        -- entering address on Square's side; we'd then backfill from the
        -- webhook payload. In the happy path the basket flow always sends one.
        ShipLabel       NVARCHAR(50)  NULL,
        ShipStreet1     NVARCHAR(200) NULL,
        ShipStreet2     NVARCHAR(200) NULL,
        ShipCity        NVARCHAR(100) NULL,
        ShipState       NVARCHAR(50)  NULL,
        ShipZip         NVARCHAR(20)  NULL,
        ShipCountry     NVARCHAR(2)   NULL CONSTRAINT DF_Order_ShipCountry DEFAULT 'US',

        CustomerNotes   NVARCHAR(MAX) NULL,

        CreatedAt       DATETIME2 NOT NULL CONSTRAINT DF_Order_CreatedAt DEFAULT SYSUTCDATETIME(),
        PaidAt          DATETIME2 NULL,
        CancelledAt     DATETIME2 NULL
    );

    CREATE INDEX IX_Order_Customer_CreatedAt
        ON cust.[Order] (CustomerId, CreatedAt DESC);

    -- Drives the Square webhook lookup — unique because Square's order_id is
    -- 1:1 with our Order row, and uniqueness lets the webhook handler do an
    -- idempotent UPDATE-by-SquareOrderId without fear of dupes.
    CREATE UNIQUE INDEX UX_Order_SquareOrderId
        ON cust.[Order] (SquareOrderId)
        WHERE SquareOrderId IS NOT NULL;
END;

-- ── 2. cust.OrderItem ───────────────────────────────────────────────────────
-- Links an Order to the Reservations it consumed. Each row also snapshots
-- inventory metadata (name, sku, unit price) so the order receipt is stable
-- against future inventory edits — same reasoning as the address snapshot.
IF OBJECT_ID('cust.OrderItem', 'U') IS NULL
BEGIN
    CREATE TABLE cust.OrderItem (
        OrderItemId     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrderItem PRIMARY KEY,
        OrderId         INT NOT NULL
            CONSTRAINT FK_OrderItem_Order REFERENCES cust.[Order](OrderId) ON DELETE CASCADE,
        ReservationId   INT NOT NULL
            CONSTRAINT FK_OrderItem_Reservation REFERENCES cust.Reservation(ReservationId),
        InventoryId     INT NOT NULL
            CONSTRAINT FK_OrderItem_Inventory REFERENCES inv.Inventory(InventoryId),

        -- Inventory snapshot at checkout time
        ItemName        NVARCHAR(200) NOT NULL,
        ItemSku         NVARCHAR(100) NULL,
        UnitPriceCents  INT NOT NULL CONSTRAINT CK_OrderItem_Price CHECK (UnitPriceCents >= 0)
    );

    CREATE INDEX IX_OrderItem_Order ON cust.OrderItem (OrderId);

    -- One Reservation maps to at most one OrderItem — same reservation row
    -- can't get checked out twice. Enforced at the DB layer because client-
    -- side guards aren't enough under the b-flow concurrency model (basket
    -- → checkout-pending → Square webhook can race with anything).
    CREATE UNIQUE INDEX UX_OrderItem_Reservation ON cust.OrderItem (ReservationId);
END;

-- ── 3. cust.Reservation status collapse + 2-day default expiry ──────────────
-- Old statuses → new mapping:
--   Pending, Confirmed, PaymentSent  → 'Active' (still in basket)
--   Cancelled                        → 'Expired' (treated the same — gone)
--   Completed                        → 'Active' if order is still in flight,
--                                       but in practice every Completed today
--                                       was a successful purchase, and those
--                                       should map to 'Expired' too because
--                                       the item is no longer in any basket.
--                                       The Order/OrderItem rows that *would*
--                                       have backed them don't exist for
--                                       legacy data — that's fine, those
--                                       reservations predate the Order table.
--   Expired                          → 'Expired' (no change)
IF NOT EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE  name = 'CK_Reservation_Status_v2'
      AND  parent_object_id = OBJECT_ID('cust.Reservation')
)
BEGIN
    -- Drop the old check constraint if it exists. The original migration
    -- named it CK_Reservation_Status; if it was created inline without a name
    -- the dynamic SQL below catches that.
    DECLARE @oldCk NVARCHAR(200);
    SELECT @oldCk = name
    FROM   sys.check_constraints
    WHERE  parent_object_id = OBJECT_ID('cust.Reservation')
      AND  name LIKE 'CK_Reservation_Status%'
      AND  name <> 'CK_Reservation_Status_v2';
    IF @oldCk IS NOT NULL
        EXEC('ALTER TABLE cust.Reservation DROP CONSTRAINT ' + @oldCk);

    -- Backfill before the new constraint goes on
    UPDATE cust.Reservation
    SET    Status = CASE
                      WHEN Status IN ('Pending','Confirmed','PaymentSent') THEN 'Active'
                      WHEN Status IN ('Cancelled','Completed','Expired')   THEN 'Expired'
                      ELSE 'Expired'
                    END;

    ALTER TABLE cust.Reservation
        ADD CONSTRAINT CK_Reservation_Status_v2 CHECK (Status IN ('Active','Expired'));
END;

-- New default expiry (2 days = 48h, restated explicitly so the basket-hold
-- window is set in one place we can find).
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints
    WHERE  name = 'DF_Reservation_ExpiresAt_v2'
      AND  parent_object_id = OBJECT_ID('cust.Reservation')
)
BEGIN
    DECLARE @oldDf NVARCHAR(200);
    SELECT @oldDf = dc.name
    FROM   sys.default_constraints dc
    JOIN   sys.columns c
           ON c.object_id    = dc.parent_object_id
          AND c.column_id    = dc.parent_column_id
    WHERE  dc.parent_object_id = OBJECT_ID('cust.Reservation')
      AND  c.name              = 'ExpiresAt'
      AND  dc.name             <> 'DF_Reservation_ExpiresAt_v2';
    IF @oldDf IS NOT NULL
        EXEC('ALTER TABLE cust.Reservation DROP CONSTRAINT ' + @oldDf);

    ALTER TABLE cust.Reservation
        ADD CONSTRAINT DF_Reservation_ExpiresAt_v2
            DEFAULT DATEADD(DAY, 2, SYSUTCDATETIME()) FOR ExpiresAt;
END;

-- The PaymentSentAt / CompletedAt / SquarePaymentLinkUrl / AmountCents
-- columns on cust.Reservation are now legacy — they all describe Order-level
-- concerns. We're NOT dropping them in this migration because dropping
-- columns is irreversible and the historical data in them is the only
-- record of pre-migration purchases. New code stops writing to them; a
-- future cleanup migration can drop them once we've confirmed nothing
-- queries them. (Tracked: tech-debt cleanup pass post-launch.)

-- ── 4. cust.Message gets an OrderId pointer ─────────────────────────────────
-- The existing nullable ReservationId column stays — that's how per-item
-- "Ask Noemi" threads (#4 in the design) anchor messages to the specific
-- piece. The new OrderId column anchors order-level auto-messages
-- ("Ordered: X, Y, Z — total $N — shipping to ..."). They're mutually
-- exclusive in practice but we don't enforce that at the DB layer; if a
-- message somehow had both we'd treat OrderId as the stronger anchor.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE  object_id = OBJECT_ID('cust.Message')
      AND  name      = 'OrderId'
)
BEGIN
    ALTER TABLE cust.Message
        ADD OrderId INT NULL
            CONSTRAINT FK_Message_Order REFERENCES cust.[Order](OrderId);
END;

COMMIT TRANSACTION;
GO

-- The index creation lives in its own batch (after the GO above) so the
-- parser sees the new OrderId column. Wrapped in IF NOT EXISTS for
-- idempotency.
SET QUOTED_IDENTIFIER ON;
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE  name      = 'IX_Message_Order'
      AND  object_id = OBJECT_ID('cust.Message')
)
BEGIN
    CREATE INDEX IX_Message_Order ON cust.Message (OrderId) WHERE OrderId IS NOT NULL;
END;