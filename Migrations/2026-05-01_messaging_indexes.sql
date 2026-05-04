-- ============================================================================
-- 2026-05-01 messaging indexes for admin-side conversation views
-- ============================================================================
-- Run once against the LinenLady database. Idempotent — safe to re-run.
--
-- Background:
--   The admin inbox at /admin/messages calls GetConversationsAsync, which
--   does a per-customer CROSS APPLY on cust.Message ordered by SentAt DESC,
--   plus a COUNT(*) WHERE Direction='Inbound' AND IsRead=0. Without these
--   indexes both lookups scan the message table per row in the customer
--   list, which gets noticeably slow once there are a few hundred messages.
--
--   No schema changes — only indexes. The cust.Message table itself already
--   carries every column the new admin endpoints touch.
-- ============================================================================

-- Latest-message-per-customer lookup (CROSS APPLY ... ORDER BY SentAt DESC)
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE  name      = 'IX_Message_Customer_SentAt_Desc'
      AND  object_id = OBJECT_ID('cust.Message')
)
BEGIN
    CREATE INDEX IX_Message_Customer_SentAt_Desc
        ON cust.Message (CustomerId, SentAt DESC)
        INCLUDE (Body, Direction);
END;

-- Unread-inbound count (admin-nav badge + per-customer summary).
-- Filtered index keeps it small — only unread inbound rows are tracked.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE  name      = 'IX_Message_UnreadInbound'
      AND  object_id = OBJECT_ID('cust.Message')
)
BEGIN
    CREATE INDEX IX_Message_UnreadInbound
        ON cust.Message (CustomerId)
        WHERE Direction = 'Inbound' AND IsRead = 0;
END;

-- Symmetric filtered index for the customer-side "Noemi replied" badge.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE  name      = 'IX_Message_UnreadOutbound'
      AND  object_id = OBJECT_ID('cust.Message')
)
BEGIN
    CREATE INDEX IX_Message_UnreadOutbound
        ON cust.Message (CustomerId)
        WHERE Direction = 'Outbound' AND IsRead = 0;
END;
