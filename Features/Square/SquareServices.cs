// Features/Square/SquareServices.cs
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LinenLady.API.Contracts;
using Microsoft.Extensions.Options;

namespace LinenLady.API.Square;

public partial interface ISquareService
{
    Task<SquarePaymentLinkResult> CreatePaymentLinkAsync(
        int                  reservationId,
        string               itemName,
        string               itemSku,
        int                  amountCents,
        string               customerEmail,
        string               customerName,
        CustomerAddressDto?  shippingAddress = null
    );
}

public partial class SquareService : ISquareService
{
    private readonly HttpClient _http;
    private readonly string     _locationId;
    private readonly string     _checkoutBaseUrl;

    public SquareService(
        IHttpClientFactory        factory,
        IOptions<SquareOptions>   options,
        IOptions<FrontendOptions> frontendOptions)
    {
        var opts         = options.Value;
        var frontendOpts = frontendOptions.Value;

        _checkoutBaseUrl = !string.IsNullOrWhiteSpace(frontendOpts.BaseUrl)
            ? frontendOpts.BaseUrl.TrimEnd('/')
            : throw new InvalidOperationException("Frontend:BaseUrl not configured.");

        _locationId = !string.IsNullOrWhiteSpace(opts.LocationId)
            ? opts.LocationId
            : throw new InvalidOperationException("Square:LocationId not configured.");

        var token = !string.IsNullOrWhiteSpace(opts.AccessToken)
            ? opts.AccessToken
            : throw new InvalidOperationException("Square:AccessToken not configured.");

        // Sandbox vs production routing. Set Square:Environment in
        // appsettings.{Env}.json or as the env var Square__Environment.
        // Sandbox is the default so a misconfigured deployment fails safe
        // (test traffic against test endpoints) rather than hitting prod.
        _http = factory.CreateClient("square");
        var baseUrl = string.Equals(opts.Environment, "Production", StringComparison.OrdinalIgnoreCase)
            ? "https://connect.squareup.com/"
            : "https://connect.squareupsandbox.com/";
        _http.BaseAddress = new Uri(baseUrl);

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.Accept
            .Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<SquarePaymentLinkResult> CreatePaymentLinkAsync(
        int                  reservationId,
        string               itemName,
        string               itemSku,
        int                  amountCents,
        string               customerEmail,
        string               customerName,
        CustomerAddressDto?  shippingAddress = null)
    {
        // Idempotency key includes the unix timestamp so retries from the
        // same reservation get distinct keys. If we ever want true at-most-once
        // semantics, swap this for `reservation-{id}` and store the resulting
        // link on the reservation row, then short-circuit on retry.
        var idempotencyKey = $"reservation-{reservationId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        var (firstName, lastName) = SplitName(customerName);

        // If we know the customer's default address, pre-fill Square's form
        // with the full structured address so they can confirm in one click.
        // If we don't, fall back to just display_name and Square will show
        // an empty form for them to fill in.
        object buyerAddress = shippingAddress is null
            ? new { display_name = customerName }
            : new
            {
                first_name                       = firstName,
                last_name                        = lastName,
                address_line_1                   = shippingAddress.Street1,
                address_line_2                   = shippingAddress.Street2 ?? "",
                locality                         = shippingAddress.City,
                administrative_district_level_1  = shippingAddress.State,
                postal_code                      = shippingAddress.Zip,
                country                          = string.IsNullOrWhiteSpace(shippingAddress.Country)
                                                     ? "US"
                                                     : shippingAddress.Country
            };

        // Square accepts either `quick_pay` (Square builds the order) or
        // `order` (we build it), never both. We use `order` because we need
        // `reference_id = "RES-{id}"` on the order itself so the webhook
        // handler can match payments back to reservations.
        var body = new
        {
            idempotency_key = idempotencyKey,
            order = new
            {
                location_id  = _locationId,
                reference_id = $"RES-{reservationId}",
                line_items = new[]
                {
                    new
                    {
                        name             = $"{itemName} ({itemSku})",
                        quantity         = "1",
                        base_price_money = new { amount = amountCents, currency = "USD" }
                    }
                }
            },
            checkout_options = new
            {
                redirect_url             = $"{_checkoutBaseUrl}/shop/reservation/{reservationId}/confirmed",
                ask_for_shipping_address = true,
            },
            pre_populated_data = new
            {
                buyer_email   = customerEmail,
                buyer_address = buyerAddress
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

        var result = await response.Content.ReadFromJsonAsync<SquareCreateLinkResponse>()
            ?? throw new InvalidOperationException("Square returned empty response.");

        return new SquarePaymentLinkResult(
            result.PaymentLink.Id,
            result.PaymentLink.Url,
            result.PaymentLink.OrderId
        );
    }

    // ── Helpers ───────────────────────────────────────────────

    private static (string first, string last) SplitName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return ("", "");
        var idx = fullName.IndexOf(' ');
        return idx < 0
            ? (fullName, "")
            : (fullName[..idx], fullName[(idx + 1)..].Trim());
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