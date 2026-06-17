namespace LinenLady.API.Features.Contact.Contracts;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Public "Contact Noemi" form submission. No auth required — anyone can send.
/// Rate-limited per IP and per FromEmail by ContactService.
/// </summary>
public sealed record ContactRequest
{
    /// <summary>Sender display name. Required, 1–120 chars.</summary>
    [Required, StringLength(120, MinimumLength = 1)]
    public string FromName { get; init; } = "";

    /// <summary>Sender email. Required, validated. Used as Reply-To so Noemi can reply directly.</summary>
    [Required, EmailAddress, StringLength(254)]
    public string FromEmail { get; init; } = "";

    /// <summary>Subject line. Optional — defaults to "Inquiry from LinenLady site".</summary>
    [StringLength(200)]
    public string? Subject { get; init; }

    /// <summary>Message body. Required, 1–4000 chars (matches cust.Message.Body cap).</summary>
    [Required, StringLength(4000, MinimumLength = 1)]
    public string Body { get; init; } = "";

    /// <summary>Optional product reference (e.g. "BLR-1923"). Surfaced in Noemi's email subject.</summary>
    [StringLength(64)]
    public string? ProductSku { get; init; }

    /// <summary>
    /// Honeypot. Real users never fill this; bots usually do.
    ///
    /// IMPORTANT: no validation attribute here, on purpose. With
    /// [StringLength(0)], [ApiController] model validation rejected any
    /// non-empty value with a 400 that NAMED this field — so the silent
    /// fake-success path in ContactService never ran, and bots were told
    /// exactly which field to leave blank. Validation-free, a filled
    /// honeypot flows through to SubmitAsync, which logs it and returns a
    /// success-shaped response without persisting or sending anything.
    /// </summary>
    public string? Website { get; init; }

    /// <summary>
    /// Cloudflare Turnstile token produced by the widget on the contact form.
    ///
    /// No validation attribute — same reasoning as the honeypot. We do NOT want
    /// [ApiController] to reject a missing token with a field-naming 400; the
    /// verification is done inside ContactService so we control the message and
    /// the failure maps to our friendly banner. A null/empty token fails
    /// verification and is rejected as a ContactValidationException (400).
    /// </summary>
    public string? TurnstileToken { get; init; }
}

public sealed record ContactResponse(
    long SubmissionId,
    string Message);
