DELETE FROM cust.ContactSubmission
WHERE FromEmail = 'jon.gracias@gmail.com'
  AND Status <> 'Sent'
  AND CreatedAt >= DATEADD(DAY, -1, SYSUTCDATETIME());