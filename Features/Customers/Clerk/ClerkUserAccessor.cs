namespace LinenLady.API.Api.Auth;

using Microsoft.AspNetCore.Http;

/// <summary>
/// Reads the Clerk user id from the X-Clerk-User-Id header, which the Next.js
/// middleware sets after validating the Clerk JWT. Returns null if the header
/// is missing or blank.
/// </summary>
public static class ClerkUserAccessor
{
    public const string HeaderName = "X-Clerk-User-Id";

    public static string? GetClerkUserId(this HttpRequest request)
    {
        if (!request.Headers.TryGetValue(HeaderName, out var values))
            return null;

        var id = values.FirstOrDefault();
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }
}
