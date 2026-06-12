namespace LinenLady.API.Contracts;

using System.Text.Json.Serialization;

/// <summary>
/// Per-item availability returned by POST /api/inventory/availability.
/// Items not in the response are implicitly Available.
/// </summary>


public sealed record ItemAvailabilityDto
{
    public int    InventoryId { get; set; }

    /// <summary>
    /// One of:
    ///   "InBasket"           - someone else holds it in their basket
    ///   "PendingPayment"     - someone else is paying via Square
    ///   "Sold"               - permanently sold to someone else
    ///   "Inactive"           - listing flag changed; shouldn't normally appear
    ///   "YourBasket"         - the caller themselves holds it
    ///   "YourPendingPayment" - the caller themselves is paying
    /// </summary>
    public string State { get; set; } = "";

    /// <summary>
    /// Internal only — which customer holds the blocking reservation/order.
    /// Used by GetAvailabilityHandler to personalize State into
    /// YourBasket / YourPendingPayment for the caller. NEVER serialized:
    /// the availability endpoint is anonymous, and exposing customer ids
    /// (and which items they're shopping) would be an information leak.
    /// </summary>
    [JsonIgnore]
    public int?   BlockingCustomerId  { get; init; }
}

public sealed class GetAvailabilityRequest
{
    public List<int> InventoryIds { get; set; } = new();
}

public sealed class GetAvailabilityResponse
{
    public List<ItemAvailabilityDto> Items { get; set; } = new();
}
