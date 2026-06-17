namespace LinenLady.API.Features.Contact.Service;

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Verifies a Cloudflare Turnstile token server-side. The contact form renders
/// the widget; on submit the browser sends the resulting token, and this
/// service confirms it with Cloudflare before any message is persisted or sent.
///
/// We deliberately do NOT send the optional "remoteip" parameter. Behind
/// SWA → App Service, RemoteIpAddress is Azure infrastructure, not the real
/// visitor, so passing it would only cause false rejections. Turnstile proves
/// "a real browser solved a challenge" without us needing a trustworthy client
/// IP at all — which is the whole reason it fits this hosting setup.
/// </summary>
public interface ITurnstileVerifier
{
    Task<bool> VerifyAsync(string? token, CancellationToken ct = default);
}

public sealed class TurnstileVerifier(
    IHttpClientFactory       httpFactory,
    IOptions<TurnstileOptions> options,
    ILogger<TurnstileVerifier> log) : ITurnstileVerifier
{
    private const string VerifyUrl =
        "https://challenges.cloudflare.com/turnstile/v0/siteverify";

    private readonly IHttpClientFactory _httpFactory = httpFactory;
    private readonly TurnstileOptions   _opts        = options.Value;
    private readonly ILogger<TurnstileVerifier> _log = log;

    public async Task<bool> VerifyAsync(string? token, CancellationToken ct = default)
    {
        // Disabled in config → treat everything as human (local dev escape hatch).
        if (!_opts.Enabled)
        {
            _log.LogWarning("Turnstile verification is DISABLED via configuration.");
            return true;
        }

        if (string.IsNullOrWhiteSpace(token))
            return false;

        var client = _httpFactory.CreateClient("turnstile");

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["secret"]   = _opts.SecretKey,
            ["response"] = token,
        });

        try
        {
            using var response = await client.PostAsync(VerifyUrl, form, ct);
            if (!response.IsSuccessStatusCode)
            {
                _log.LogWarning(
                    "Turnstile siteverify returned non-success status {Status}",
                    (int)response.StatusCode);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<TurnstileResponse>(ct);

            if (result is { Success: false } && result.ErrorCodes is { Length: > 0 })
            {
                // error-codes are diagnostic only — never surfaced to the user.
                _log.LogInformation(
                    "Turnstile verification failed: {Codes}",
                    string.Join(",", result.ErrorCodes));
            }

            return result?.Success == true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Network blip or timeout calling Cloudflare. Fail closed — a
            // submission that can't be verified is not let through.
            _log.LogWarning(ex, "Turnstile siteverify call failed");
            return false;
        }
    }

    private sealed record TurnstileResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("error-codes")]
        public string[]? ErrorCodes { get; init; }
    }
}
