namespace LinenLady.API.Auth;

using System.Security.Claims;
using System.Text.Json;

/// <summary>
/// Convenience accessors for Clerk-issued JWT claims. Replaces the old
/// <c>ClerkUserAccessor</c> header-based approach — claims are now read
/// from the validated principal populated by the JWT bearer handler.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    // Clerk JWT claim names. "sub" is standard; the rest come from Clerk's
    // default JWT template. If a custom template is in use, these must match.
    public const string EmailClaim          = "email";
    public const string EmailVerifiedClaim  = "email_verified";
    public const string OrgIdClaim          = "org_id";   // v1 / custom-claim fallback
    public const string OrgClaim            = "o";        // v2 nested object

    /// <summary>
    /// Clerk user id (JWT <c>sub</c> claim). Returns null if the principal
    /// is unauthenticated or the claim is missing — callers that require
    /// authentication should rely on <c>[Authorize]</c> to reject those cases
    /// before this runs, but the null return is kept for defense in depth.
    /// </summary>
    public static string? GetClerkUserId(this ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true) return null;
        var sub = user.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? user.FindFirstValue("sub");
        return string.IsNullOrWhiteSpace(sub) ? null : sub;
    }

    /// <summary>
    /// Primary email from the JWT. Trustworthy because the JWT is signed by Clerk.
    /// </summary>
    public static string? GetEmail(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.Email)
        ?? user.FindFirstValue(EmailClaim);

    /// <summary>
    /// Whether Clerk has verified the user's email. Sourced from the JWT,
    /// not the request body — this closes the email-verification spoof vector.
    /// </summary>
    public static bool GetEmailVerified(this ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(EmailVerifiedClaim);
        return bool.TryParse(raw, out var verified) && verified;
    }

    /// <summary>
    /// Active Clerk organization id on the JWT, if any. Used by the admin policy.
    ///
    /// Clerk session token v2 (April 2025) nests organization info under the
    /// "o" claim as a JSON object: <c>{"id":"org_...","rol":"admin","slg":"..."}</c>.
    /// v1 emits a top-level "org_id" string. We try v2 first, then fall back to
    /// v1 so the API works against either token format — and so a custom
    /// <c>{"org_id":"{{org.id}}"}</c> claim template still resolves if added.
    /// </summary>
    public static string? GetOrgId(this ClaimsPrincipal user)
    {
        var oClaim = user.FindFirstValue(OrgClaim);
        if (!string.IsNullOrWhiteSpace(oClaim))
        {
            try
            {
                using var doc = JsonDocument.Parse(oClaim);
                if (doc.RootElement.TryGetProperty("id", out var idEl))
                {
                    var id = idEl.GetString();
                    if (!string.IsNullOrWhiteSpace(id)) return id;
                }
            }
            catch (JsonException)
            {
                // Malformed "o" claim — fall through to v1 lookup.
            }
        }

        var legacy = user.FindFirstValue(OrgIdClaim);
        return string.IsNullOrWhiteSpace(legacy) ? null : legacy;
    }
}