namespace LinenLady.API.Auth;

/// <summary>
/// Configuration for Clerk JWT validation. Bound from the "Clerk" section
/// of configuration at startup. The API validates Clerk-issued JWTs against
/// Clerk's JWKS endpoint (discovered via OIDC metadata at {Authority}/.well-known/openid-configuration).
///
/// Admin identity is org-membership based: a user is considered an admin if
/// their JWT carries an <c>org_id</c> claim whose value equals <see cref="AdminOrgId"/>.
/// This matches the Next.js middleware's admin check (org membership, not role metadata).
/// </summary>
public sealed class ClerkAuthOptions
{
    public const string SectionName = "Clerk";

    /// <summary>
    /// The Clerk instance URL that issues and signs JWTs, e.g.
    /// "https://your-instance.clerk.accounts.dev" or a custom domain.
    /// This is used as both the JWT <c>iss</c> and the OIDC discovery root.
    /// </summary>
    public string Authority { get; set; } = "";

    /// <summary>
    /// Clerk organization id whose members are treated as admins.
    /// Corresponds to the <c>ADMIN_ORG_ID</c> env var used by the Next.js middleware.
    /// JWTs must include this value in their <c>org_id</c> claim (requires a Clerk
    /// JWT template that emits the organization claim).
    /// </summary>
    public string AdminOrgId { get; set; } = "";
}
