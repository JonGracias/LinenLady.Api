namespace LinenLady.API.Features.Contact;

/// <summary>
/// Bound from the "Contact" config section.
/// Keep secrets (ResendApiKey) in user-secrets / Azure App Service config, not in appsettings.json.
/// </summary>
public sealed class ContactOptions
{
    public const string SectionName = "Contact";

    /// <summary>Where Noemi receives inquiries. e.g. "noemi@linenlady.net".</summary>
    public string RecipientEmail { get; set; } = "";

    /// <summary>Display name shown to Noemi for the recipient ("Noemi" / "LinenLady"). Cosmetic.</summary>
    public string RecipientName { get; set; } = "Noemi";

    /// <summary>From-address on outbound mail. Must be on a domain you've verified with Resend (DKIM/SPF). e.g. "noreply@linenlady.net".</summary>
    public string SenderEmail { get; set; } = "jon.gracias@gmail.com";

    /// <summary>From-display name. Final header reads as: "{visitor} via LinenLady &lt;noreply@linenlady.net&gt;".</summary>
    public string SenderBrand { get; set; } = "LinenLady";

    /// <summary>Resend API key (re_xxx). Required.</summary>
    public string ResendApiKey { get; set; } = "";

    /// <summary>Max submissions per IP address per hour. 0 = disabled.</summary>
    public int MaxPerIpPerHour { get; set; } = 5;

    /// <summary>Max submissions per FromEmail per day. 0 = disabled.</summary>
    public int MaxPerEmailPerDay { get; set; } = 10;

    /// <summary>If true, also send the visitor a confirmation email ("we got your message"). Default true.</summary>
    public bool SendVisitorConfirmation { get; set; } = true;
}
