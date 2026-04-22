namespace LinenLady.API.Api.Auth;

using System.Security.Claims;

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
    public const string OrgIdClaim          = "org_id";

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
    /// Clerk organization id on the JWT, if any. Used by the admin policy.
    /// </summary>
    public static string? GetOrgId(this ClaimsPrincipal user)
        => user.FindFirstValue(OrgIdClaim);
}
