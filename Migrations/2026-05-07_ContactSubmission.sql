-- ============================================================================
-- 2026-05-07  cust.ContactSubmission
-- ============================================================================
-- Audit log for the public POST /api/contact endpoint.
-- Each row is one submission attempt: pending until the email provider acks,
-- then Sent (with provider message id) or Failed (with error message).
--
-- Status values:
--   'Pending'  inserted but not yet sent (only seen mid-request)
--   'Sent'     provider returned 2xx
--   'Failed'   provider returned an error or threw
--
-- The IpAddress + CreatedAt index supports per-IP rate-limit lookups.
-- The FromEmail + CreatedAt index supports per-email rate-limit lookups.
-- Idempotent — safe to re-run.
-- ============================================================================

IF NOT EXISTS (
    SELECT 1 FROM sys.tables
    WHERE  name      = 'ContactSubmission'
      AND  schema_id = SCHEMA_ID('cust')
)
BEGIN
    CREATE TABLE cust.ContactSubmission (
        SubmissionId       BIGINT          NOT NULL IDENTITY(1,1),
        FromName           NVARCHAR(120)   NOT NULL,
        FromEmail          NVARCHAR(254)   NOT NULL,
        Subject            NVARCHAR(200)   NULL,
        Body               NVARCHAR(4000)  NOT NULL,
        ProductSku         NVARCHAR(64)    NULL,
        IpAddress          NVARCHAR(45)    NULL,        -- IPv6-safe length
        UserAgent          NVARCHAR(500)   NULL,
        Status             NVARCHAR(16)    NOT NULL DEFAULT 'Pending',
        ProviderMessageId  NVARCHAR(120)   NULL,
        ErrorMessage       NVARCHAR(500)   NULL,
        CreatedAt          DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
        SentAt             DATETIME2       NULL,

        CONSTRAINT PK_ContactSubmission PRIMARY KEY (SubmissionId),
        CONSTRAINT CK_ContactSubmission_Status
            CHECK (Status IN ('Pending','Sent','Failed'))
    );
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE  name      = 'IX_ContactSubmission_Ip_CreatedAt'
      AND  object_id = OBJECT_ID('cust.ContactSubmission')
)
BEGIN
    CREATE INDEX IX_ContactSubmission_Ip_CreatedAt
        ON cust.ContactSubmission (IpAddress, CreatedAt DESC)
        WHERE IpAddress IS NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE  name      = 'IX_ContactSubmission_Email_CreatedAt'
      AND  object_id = OBJECT_ID('cust.ContactSubmission')
)
BEGIN
    CREATE INDEX IX_ContactSubmission_Email_CreatedAt
        ON cust.ContactSubmission (FromEmail, CreatedAt DESC);
END;
GO

PRINT 'cust.ContactSubmission migration completed.';
