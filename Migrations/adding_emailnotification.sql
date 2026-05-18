-- migrations/20260515_order_notification_email_sent_at.sql
--
-- Sentinel column for "we've sent the order-paid email to Noemi for this
-- order." NULL = not yet sent; SYSUTCDATETIME() once the webhook handler
-- has dispatched the email.
--
-- Why a column and not an in-memory flag: Square's payment.updated
-- webhook is at-least-once delivery, and 'COMPLETED' fires for multiple
-- transitions on the payment object. Without a durable sentinel a
-- second-delivered webhook would call MarkOrderPaidAsync, see "already
-- Paid" via the GetOrderBySquareIdAsync fallback, return a non-null
-- OrderDto, and we'd send Noemi a second copy of the email.
--
-- The atomic flip lives in CustomerRepository.TryClaimOrderPaidEmailAsync:
-- an UPDATE...OUTPUT...WHERE NotificationEmailSentAt IS NULL returns
-- the OrderId only for the writer that won the race.

ALTER TABLE cust.[Order]
    ADD NotificationEmailSentAt DATETIME2(7) NULL;
GO