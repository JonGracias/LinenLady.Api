namespace LinenLady.API.Auth;

/// <summary>
/// Authorization policy names. Used with <c>[Authorize(Policy = ...)]</c>
/// on controllers and actions.
/// </summary>
public static class AuthPolicies
{
    /// <summary>Any authenticated Clerk user. Applies to customer-owned data.</summary>
    public const string Customer = "Customer";

    /// <summary>
    /// Authenticated user whose JWT carries an <c>org_id</c> claim matching
    /// <c>Clerk:AdminOrgId</c> from configuration. Applies to mutation endpoints
    /// on inventory, site content, images, and AI features.
    /// </summary>
    public const string Admin = "Admin";
}
