namespace LinenLady.API.Features.Contact;

/// <summary>
/// Bound from the "Turnstile" config section. Drives the bot check on the
/// public contact form.
///
/// Keep SecretKey in user-secrets / Azure App Service config (Turnstile__SecretKey),
/// never in appsettings.json — it's as sensitive as the Resend key.
/// </summary>
public sealed class TurnstileOptions
{
    public const string SectionName = "Turnstile";

    /// <summary>
    /// Cloudflare Turnstile secret key (server-side). Required when Enabled.
    /// For local dev, Cloudflare's always-pass test secret is
    /// "1x0000000000000000000000000000000AA".
    /// </summary>
    public string SecretKey { get; set; } = "";

    /// <summary>
    /// Master switch. Defaults to true. Set false in environments where you
    /// don't want the check (e.g. a throwaway local run with no keys at all);
    /// the verifier then short-circuits to "passed".
    /// </summary>
    public bool Enabled { get; set; } = true;
}
