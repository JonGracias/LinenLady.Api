// Features/Square/SquareService.order.cs
//
// New overload of CreatePaymentLinkAsync that takes an OrderDto with N
// line items, references "ORD-{id}" instead of "RES-{id}" so the webhook
// can route to MarkOrderPaidAsync, and pre-fills the buyer address from
// the order's shipping snapshot.
//
// The original CreatePaymentLinkAsync(reservationId, ...) overload stays —
// nothing in the new code path calls it, but removing it breaks any
// pre-migration consumer that might still be running. Plan: drop in the
// same future migration that drops the legacy reservation columns.

namespace LinenLady.API.Square;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LinenLady.API.Contracts;

public partial interface ISquareService
{
    Task<SquarePaymentLinkResult> CreatePaymentLinkForOrderAsync(
        OrderDto order,
        string   customerEmail,
        string   customerName);
}

public partial class SquareService : ISquareService
{
    public async Task<SquarePaymentLinkResult> CreatePaymentLinkForOrderAsync(
        OrderDto order,
        string   customerEmail,
        string   customerName)
    {
        // Idempotency: order-level retries should be at-most-once per attempt.
        // Including the unix timestamp keeps retries from getting wedged on
        // the same key if Square ever rejects mid-flight. If we want strict
        // at-most-once, swap to $"order-{order.OrderId}" and short-circuit on
        // already-set SquarePaymentLinkId — same pattern as the reservation
        // overload's TODO.
        var idempotencyKey = $"order-{order.OrderId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

        var (firstName, lastName) = SplitName(customerName);

        // Pre-fill from the order's snapshotted address (which itself was
        // copied from cust.CustomerAddress at checkout). Falls back to a
        // bare display_name only if every shipping field is null — which
        // shouldn't happen post-checkout but the guard costs nothing.
        object buyerAddress = string.IsNullOrWhiteSpace(order.ShipStreet1)
            ? new { display_name = customerName }
            : new
            {
                first_name                       = firstName,
                last_name                        = lastName,
                address_line_1                   = order.ShipStreet1,
                address_line_2                   = order.ShipStreet2 ?? "",
                locality                         = order.ShipCity,
                administrative_district_level_1  = order.ShipState,
                postal_code                      = order.ShipZip,
                country                          = string.IsNullOrWhiteSpace(order.ShipCountry)
                                                     ? "US"
                                                     : order.ShipCountry
            };

        // Build the Square line items from order items. Each gets the
        // snapshotted name + sku + price — Square sees what the customer
        // sees at checkout, even if inventory rows change later.
        var lineItems = order.Items.Select(i => new
        {
            name             = string.IsNullOrWhiteSpace(i.ItemSku)
                                  ? i.ItemName
                                  : $"{i.ItemName} ({i.ItemSku})",
            quantity         = "1",
            base_price_money = new { amount = i.UnitPriceCents, currency = "USD" }
        }).ToArray();

        var body = new
        {
            idempotency_key = idempotencyKey,
            order = new
            {
                location_id  = _locationId,
                reference_id = $"ORD-{order.OrderId}",   // <— webhook routing
                line_items   = lineItems
            },
            checkout_options = new
            {
                redirect_url             = $"{_checkoutBaseUrl}/account?tab=orders&placed={order.OrderId}",
                ask_for_shipping_address = false,         // we already have it
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
            throw new InvalidOperationException(
                $"Square API error {(int)response.StatusCode}: {err}");
        }

        var result = await response.Content.ReadFromJsonAsync<SquareCreateLinkResponse>()
            ?? throw new InvalidOperationException("Square returned empty response.");

        return new SquarePaymentLinkResult(
            result.PaymentLink.Id,
            result.PaymentLink.Url,
            result.PaymentLink.OrderId);
    }
}
