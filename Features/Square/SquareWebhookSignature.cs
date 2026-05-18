namespace LinenLady.API.Square;

using System.Security.Cryptography;
using System.Text;

public static class SquareWebhookSignature
{
    public static bool IsValid(
        string signatureKey,
        string notificationUrl,
        string rawBody,
        string? providedSignature)
    {
        if (string.IsNullOrEmpty(providedSignature))
            return false;

        var payload = notificationUrl + rawBody;
        var key = Encoding.UTF8.GetBytes(signatureKey);

        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var computed = Convert.ToBase64String(hash);

        // Constant-time comparison — don't use string ==
        var computedBytes = Encoding.UTF8.GetBytes(computed);
        var providedBytes = Encoding.UTF8.GetBytes(providedSignature);
        return CryptographicOperations.FixedTimeEquals(computedBytes, providedBytes);
    }
}