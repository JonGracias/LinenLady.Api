// Infrastructure/SquareService.cs
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LinenLady.API.Contracts;
using Microsoft.Extensions.Options;

namespace LinenLady.API.Square;

public interface ISquareService
{
    Task<SquarePaymentLinkResult> CreatePaymentLinkAsync(
        int    reservationId,
        string itemName,
        string itemSku,
        int    amountCents,
        string customerEmail,
        string customerName
    );
}

public class SquareService : ISquareService
{
    private readonly HttpClient _http;
    private readonly string     _locationId;

    // Set SQUARE_ACCESS_TOKEN and SQUARE_LOCATION_ID in local.settings.json
    // and in Azure Function App configuration.
    public SquareService(
        IHttpClientFactory factory,
        IOptions<SquareOptions> options)
    {
        var opts = options.Value;

        _locationId = !string.IsNullOrWhiteSpace(opts.LocationId)
            ? opts.LocationId
            : throw new InvalidOperationException("Square:LocationId not configured.");

        var token = !string.IsNullOrWhiteSpace(opts.AccessToken)
            ? opts.AccessToken
            : throw new InvalidOperationException("Square:AccessToken not configured.");

        _http = factory.CreateClient("square");
        _http.BaseAddress = new Uri("https://connect.squareup.com/");
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.Accept
            .Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<SquarePaymentLinkResult> CreatePaymentLinkAsync(
        int    reservationId,
        string itemName,
        string itemSku,
        int    amountCents,
        string customerEmail,
        string customerName)
    {
        var idempotencyKey = $"reservation-{reservationId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        var body = new
        {
            idempotency_key = idempotencyKey,
            quick_pay = new
            {
                name        = $"{itemName} ({itemSku})",
                price_money = new { amount = amountCents, currency = "USD" },
                location_id = _locationId
            },
            checkout_options = new
            {
                redirect_url             = $"https://linenlady.net/shop/reservation/{reservationId}/confirmed",
                ask_for_shipping_address = true,
            },
            pre_populated_data = new
            {
                buyer_email   = customerEmail,
                buyer_address = new { display_name = customerName }
            },
            order = new
            {
                reference_id = $"RES-{reservationId}",
                location_id  = _locationId,
            }
        };

        var json     = JsonSerializer.Serialize(body);
        var content  = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync("v2/online-checkout/payment-links", content);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Square API error {(int)response.StatusCode}: {err}");
        }

        // System.Net.Http.Json provides ReadFromJsonAsync on HttpContent
        var result = await response.Content.ReadFromJsonAsync<SquareCreateLinkResponse>()
            ?? throw new InvalidOperationException("Square returned empty response.");

        return new SquarePaymentLinkResult(
            result.PaymentLink.Id,
            result.PaymentLink.Url,
            result.PaymentLink.OrderId
        );
    }

    // ── Square response shape ─────────────────────────────────

    private record SquareCreateLinkResponse(
        [property: JsonPropertyName("payment_link")]
        SquarePaymentLink PaymentLink
    );

    private record SquarePaymentLink(
        [property: JsonPropertyName("id")]       string Id,
        [property: JsonPropertyName("url")]      string Url,
        [property: JsonPropertyName("order_id")] string OrderId
    );
}