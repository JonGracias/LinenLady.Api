namespace LinenLady.API.Features.Contact.Service;

using System.Text;
using LinenLady.API.Features.Contact.Contracts;
using LinenLady.API.Features.Email;
using LinenLady.API.Features.Contact.Sql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public interface IContactService
{
    Task<ContactResponse> SubmitAsync(
        ContactRequest req,
        string?        ip,
        string?        userAgent,
        CancellationToken ct = default);
}

public sealed class ContactService(
    IContactRepository    repo,
    IEmailSender          email,
    ITurnstileVerifier    turnstile,
    IOptions<ContactOptions> options,
    ILogger<ContactService>  log) : IContactService
{
    private readonly IContactRepository _repo      = repo;
    private readonly IEmailSender       _email     = email;
    private readonly ITurnstileVerifier _turnstile = turnstile;
    private readonly ContactOptions     _opts      = options.Value;
    private readonly ILogger<ContactService> _log = log;

    public async Task<ContactResponse> SubmitAsync(
        ContactRequest req,
        string?        ip,
        string?        userAgent,
        CancellationToken ct = default)
    {
        // 1. Honeypot — if present, look like success but drop silently.
        if (!string.IsNullOrEmpty(req.Website))
        {
            _log.LogInformation("Honeypot tripped from {Ip}", ip);
            return new ContactResponse(0, "Thanks — your message has been sent.");
        }

        // 1.5 Bot check — verify the Cloudflare Turnstile token before doing any
        //      work. Runs AFTER the honeypot so obvious bots are dropped without
        //      a siteverify round-trip. A missing/invalid token fails closed as a
        //      400 (ContactValidationException → friendly banner). Skipped only
        //      when Turnstile is disabled in config.
        var human = await _turnstile.VerifyAsync(req.TurnstileToken, ct);
        if (!human)
            throw new ContactValidationException(
                "We couldn't verify you're human. Please complete the check and try again.");

        // 2. Normalize + sanity-check inputs beyond DataAnnotations.
        var fromEmail = req.FromEmail.Trim().ToLowerInvariant();
        var fromName  = req.FromName.Trim();
        var body      = req.Body.Trim();
        var subject   = string.IsNullOrWhiteSpace(req.Subject)
            ? BuildDefaultSubject(fromName, req.ProductSku)
            : req.Subject!.Trim();

        if (body.Length == 0)
            throw new ContactValidationException("Message body is required.");

        // 3. Rate-limit. Fail closed with 429 — message never persists.
        await EnforceRateLimitsAsync(ip, fromEmail, ct);

        // 4. Persist as Pending. We log every attempt, even ones that fail to send,
        //    so spam patterns and provider outages are visible later.
        var submissionId = await _repo.InsertAsync(new ContactSubmissionRow(
            FromName:   fromName,
            FromEmail:  fromEmail,
            Subject:    subject,
            Body:       body,
            ProductSku: string.IsNullOrWhiteSpace(req.ProductSku) ? null : req.ProductSku!.Trim(),
            IpAddress:  ip,
            UserAgent:  Truncate(userAgent, 500)
        ), ct);

        // 5. Send to Noemi. If this throws, mark Failed and rethrow as 502.
        try
        {
            var providerId = await _email.SendAsync(BuildOwnerEmail(fromName, fromEmail, subject, body, req.ProductSku), ct);
            await _repo.MarkSentAsync(submissionId, providerId, ct);
            _log.LogInformation("Contact submission {Id} delivered ({ProviderId})", submissionId, providerId);
        }
        catch (EmailSendException ex)
        {
            // Audit-log the failure on a FRESH cancellation token. The original
            // request token may already be cancelled (slow client, timeout, the
            // 502 response we're about to send), but we still want this row in
            // the database so we can see provider outages later. 5-second budget
            // is generous — enough to survive a slow connection without holding
            // up the response any longer than necessary.
            //
            // The defensive try/catch around it is critical: if MarkFailedAsync
            // itself throws (Dapper TaskCanceledException, SQL transient, etc.),
            // we must NOT let that exception escape. The user-facing
            // ContactProviderException below carries the message that maps to
            // the friendly 502 banner; if we let an audit-log failure bubble up,
            // it gets to the controller as an unhandled exception and the user
            // sees a generic 500 instead.
            using var auditCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _repo.MarkFailedAsync(submissionId, ex.Message, auditCts.Token);
            }
            catch (Exception logEx)
            {
                _log.LogError(logEx,
                    "Failed to mark submission {Id} as Failed (provider error was: {ProviderError})",
                    submissionId, ex.Message);
            }

            // Don't leak provider error text to the caller.
            throw new ContactProviderException(
                "We couldn't send your message right now. Please try again in a few minutes.");
        }

        // 6. Optionally send the visitor a confirmation. Best-effort — failures
        //    are logged, not surfaced. Same fresh-token treatment in case the
        //    request is winding down.
        if (_opts.SendVisitorConfirmation)
        {
            using var confirmCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await _email.SendAsync(BuildVisitorConfirmation(fromName, fromEmail, subject), confirmCts.Token);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Visitor confirmation failed for submission {Id}", submissionId);
            }
        }

        return new ContactResponse(submissionId, "Thanks — your message has been sent.");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task EnforceRateLimitsAsync(string? ip, string email, CancellationToken ct)
    {
        if (_opts.DisableRateLimits)
        {
            _log.LogWarning("Contact rate limits are DISABLED via configuration.");
            return;
        }
        var now = DateTime.UtcNow;

        if (_opts.MaxPerIpPerHour > 0 && !string.IsNullOrEmpty(ip))
        {
            var ipCount = await _repo.CountByIpSinceAsync(ip, now.AddHours(-1), ct);
            if (ipCount >= _opts.MaxPerIpPerHour)
                throw new ContactRateLimitedException(
                    "Too many messages from your network. Please try again later.");
        }

        if (_opts.MaxPerEmailPerDay > 0)
        {
            var emCount = await _repo.CountByEmailSinceAsync(email, now.AddDays(-1), ct);
            if (emCount >= _opts.MaxPerEmailPerDay)
                throw new ContactRateLimitedException(
                    "You've reached today's message limit. Please try again tomorrow.");
        }
    }

    private string BuildDefaultSubject(string fromName, string? sku) =>
        string.IsNullOrWhiteSpace(sku)
            ? $"Inquiry from {fromName}"
            : $"Inquiry from {fromName} — {sku}";

    private EmailMessage BuildOwnerEmail(
        string fromName, string fromEmail, string subject, string body, string? sku)
    {
        var senderDisplay = $"{fromName} via {_opts.SenderBrand}";

        var (html, text) = RenderOwnerBody(fromName, fromEmail, body, sku);

        return new EmailMessage(
            ToEmail:      _opts.RecipientEmail,
            ToName:       _opts.RecipientName,
            FromEmail:    _opts.SenderEmail,
            FromName:     senderDisplay,
            Subject:      subject,
            HtmlBody:     html,
            TextBody:     text,
            ReplyToEmail: fromEmail,
            ReplyToName:  fromName);
    }

    private EmailMessage BuildVisitorConfirmation(string toName, string toEmail, string originalSubject)
    {
        const string body =
            "Thanks for reaching out to LinenLady. Noemi typically responds within a day or two.\n\n" +
            "If your message is time-sensitive, you'll hear back sooner.\n\n" +
            "— LinenLady";

        var html =
            "<p>Thanks for reaching out to LinenLady. Noemi typically responds within a day or two.</p>" +
            "<p>If your message is time-sensitive, you'll hear back sooner.</p>" +
            "<p>— LinenLady</p>";

        return new EmailMessage(
            ToEmail:   toEmail,
            ToName:    toName,
            FromEmail: _opts.SenderEmail,
            FromName:  _opts.SenderBrand,
            Subject:   $"We got your message — {originalSubject}",
            HtmlBody:  html,
            TextBody:  body);
    }

    private static (string html, string text) RenderOwnerBody(
        string fromName, string fromEmail, string body, string? sku)
    {
        var safeBody = System.Net.WebUtility.HtmlEncode(body).Replace("\n", "<br>");
        var safeName = System.Net.WebUtility.HtmlEncode(fromName);
        var safeMail = System.Net.WebUtility.HtmlEncode(fromEmail);
        var skuLine  = string.IsNullOrWhiteSpace(sku)
            ? ""
            : $"<p style='color:#6b6358;font-size:13px;'>About item: <strong>{System.Net.WebUtility.HtmlEncode(sku)}</strong></p>";

        var html = $"""
            <div style="font-family:Georgia,serif;max-width:560px;color:#3a342e;">
              <p style="color:#6b6358;font-size:13px;margin-bottom:4px;">From {safeName} &lt;{safeMail}&gt;</p>
              {skuLine}
              <hr style="border:none;border-top:1px solid #d8cfc4;margin:12px 0;">
              <p style="white-space:pre-wrap;line-height:1.55;">{safeBody}</p>
              <hr style="border:none;border-top:1px solid #d8cfc4;margin:18px 0;">
              <p style="color:#6b6358;font-size:12px;">Reply directly to this email — it goes to {safeName}.</p>
            </div>
            """;

        var sb = new StringBuilder();
        sb.AppendLine($"From: {fromName} <{fromEmail}>");
        if (!string.IsNullOrWhiteSpace(sku)) sb.AppendLine($"About item: {sku}");
        sb.AppendLine();
        sb.AppendLine(body);
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine($"Reply directly to this email — it goes to {fromName}.");
        return (html, sb.ToString());
    }

    private static string? Truncate(string? s, int max) =>
        s is null ? null : (s.Length <= max ? s : s[..max]);
}