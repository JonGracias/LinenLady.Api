// Features/Orders/Options/OrdersOptions.cs
namespace LinenLady.API.Features.Orders;

/// <summary>
/// Bound from the "Orders" config section.
///
/// Held separately from ContactOptions even though the recipient is the
/// same person today — keeping the two flows' configuration independent
/// means a future "send order paid emails to a fulfillment helper, but
/// keep customer inquiries going to Noemi" change is a config edit, not
/// a refactor.
/// </summary>
public sealed class OrdersOptions
{
    public const string SectionName = "Orders";

    /// <summary>Where order-paid notifications go. e.g. "noemi@linenlady.net".</summary>
    public string RecipientEmail { get; set; } = "";

    /// <summary>Display name for the recipient header. Cosmetic.</summary>
    public string RecipientName { get; set; } = "Noemi";

    /// <summary>Public storefront origin used to build "view this order" links in the email body. e.g. "https://noemithelinenlady.net".</summary>
    public string StorefrontOrigin { get; set; } = "";
}