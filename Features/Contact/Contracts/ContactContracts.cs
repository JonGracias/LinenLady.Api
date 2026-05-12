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

    /// <summary>Honeypot. Real users never fill this; bots usually do. Must be empty.</summary>
    [StringLength(0)]
    public string? Website { get; init; }
}

public sealed record ContactResponse(
    long SubmissionId,
    string Message);
