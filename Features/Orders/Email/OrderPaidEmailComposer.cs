// Features/Orders/Email/OrderPaidEmailComposer.cs
namespace LinenLady.API.Features.Orders.Email;

using System.Net;
using System.Text;
using LinenLady.API.Contracts;
using LinenLady.API.Features.Contact;       // ContactOptions: sender info
using LinenLady.API.Features.Email;         // EmailMessage
using Microsoft.Extensions.Options;

/// <summary>
/// Builds the order-paid email sent to Noemi. Mirrors ContactService's
/// composer pattern — feature-owned, transport-agnostic, returns an
/// EmailMessage the caller hands to IEmailSender.
///
/// Pure: no I/O, no DB, no logging. Easy to unit-test by passing in a
/// fabricated OrderDto.
/// </summary>
public sealed class OrderPaidEmailComposer(
    IOptions<OrdersOptions>  ordersOptions,
    IOptions<ContactOptions> contactOptions)
{
    private readonly OrdersOptions  _orders  = ordersOptions.Value;
    private readonly ContactOptions _contact = contactOptions.Value;

    /// <summary>
    /// Build the order-paid email for Noemi.
    /// </summary>
    /// <param name="order">Fully hydrated order — must include Items and shipping snapshot.</param>
    /// <param name="customerEmail">For Reply-To, so Noemi can reply straight to the buyer.</param>
    /// <param name="customerName">Buyer's display name for the body.</param>
    public EmailMessage Build(
        OrderDto order,
        string   customerEmail,
        string   customerName)
    {
        var subject = BuildSubject(order);
        var (html, text) = RenderBody(order, customerName, customerEmail);

        return new EmailMessage(
            ToEmail:      _orders.RecipientEmail,
            ToName:       _orders.RecipientName,
            FromEmail:    _contact.SenderEmail,
            FromName:     _contact.SenderBrand,
            Subject:      subject,
            HtmlBody:     html,
            TextBody:     text,
            // Reply goes to the customer — same affordance the contact
            // email uses for inquiries.
            ReplyToEmail: customerEmail,
            ReplyToName:  customerName);
    }

    /// <summary>
    /// Builds the ACTION-NEEDED alert for the edge case where a customer
    /// pays a stale Square link AFTER the order was cancelled (timeout
    /// sweep or self-serve cancel). Money has moved; the order has not
    /// been resurrected; the items may be in another customer's basket.
    /// Noemi needs to refund through the Square dashboard.
    /// </summary>
    public EmailMessage BuildPaymentAfterCancellationAlert(
        OrderDto order,
        string   customerEmail,
        string   customerName)
    {
        var total   = $"${order.AmountCents / 100m:0.00}";
        var subject = $"ACTION NEEDED — payment received on cancelled Order #{order.OrderId} ({total})";

        // ── Plaintext ───────────────────────────────────────────────
        var sb = new StringBuilder();
        sb.AppendLine($"A payment of {total} just came through for Order #{order.OrderId},");
        sb.AppendLine($"but that order was already cancelled ({order.Status}).");
        sb.AppendLine();
        sb.AppendLine("This usually means the customer paid from an old checkout link");
        sb.AppendLine("(browser tab or email) after the order timed out or was cancelled.");
        sb.AppendLine();
        sb.AppendLine("The order has NOT been reactivated — its pieces may already be in");
        sb.AppendLine("another customer's basket or sold. A refund through the Square");
        sb.AppendLine("dashboard is needed.");
        sb.AppendLine();
        sb.AppendLine($"Customer: {customerName} <{customerEmail}>");
        sb.AppendLine($"Amount:   {total}");
        if (!string.IsNullOrWhiteSpace(order.SquareOrderId))
            sb.AppendLine($"Square order id: {order.SquareOrderId}");
        if (order.CancelledAt is not null)
            sb.AppendLine($"Order cancelled at: {order.CancelledAt.Value:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();
        sb.AppendLine("Items on the cancelled order:");
        foreach (var i in order.Items)
        {
            var sku = string.IsNullOrWhiteSpace(i.ItemSku) ? "" : $" (SKU {i.ItemSku})";
            sb.AppendLine($"  • {i.ItemName}{sku} — ${i.UnitPriceCents / 100m:0.00}");
        }
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine($"Reply directly to this email to reach {customerName}.");

        // ── HTML ────────────────────────────────────────────────────
        var safeName = WebUtility.HtmlEncode(customerName);
        var safeMail = WebUtility.HtmlEncode(customerEmail);

        var itemsHtml = string.Join("", order.Items.Select(i =>
        {
            var skuHtml = string.IsNullOrWhiteSpace(i.ItemSku)
                ? ""
                : $" <span style='color:#6b6358;'>(SKU {WebUtility.HtmlEncode(i.ItemSku)})</span>";
            return $"<li>{WebUtility.HtmlEncode(i.ItemName)}{skuHtml} — <strong>${i.UnitPriceCents / 100m:0.00}</strong></li>";
        }));

        var squareIdLine = string.IsNullOrWhiteSpace(order.SquareOrderId)
            ? ""
            : $"""
              <p style="color:#6b6358;font-size:13px;margin:4px 0 0;">
                Square order id: <code>{WebUtility.HtmlEncode(order.SquareOrderId)}</code>
              </p>
              """;

        var html = $"""
            <div style="font-family:Georgia,serif;max-width:560px;color:#3a342e;">
              <p style="background:#fbeaea;border-left:3px solid #9b2c3d;padding:10px 12px;
                        color:#9b2c3d;font-size:14px;margin:0 0 14px;">
                <strong>Action needed:</strong> a payment of <strong>{total}</strong>
                came through for Order <strong>#{order.OrderId}</strong> — but that
                order was already cancelled.
              </p>

              <p style="line-height:1.55;">
                The customer most likely paid from an old checkout link after the
                order timed out or was cancelled. The order has <strong>not</strong>
                been reactivated — its pieces may already be in another customer's
                basket or sold. Please issue a refund through the Square dashboard.
              </p>

              <hr style="border:none;border-top:1px solid #d8cfc4;margin:12px 0;">

              <p style="color:#6b6358;font-size:13px;margin-bottom:4px;">
                From {safeName} &lt;{safeMail}&gt;
              </p>
              {squareIdLine}

              <p style="color:#6b6358;font-size:13px;margin-top:18px;margin-bottom:4px;">Items on the cancelled order</p>
              <ul style="margin:0;padding-left:18px;line-height:1.6;">{itemsHtml}</ul>

              <p style="margin-top:14px;font-size:15px;">
                <strong>Amount to refund: {total}</strong>
              </p>

              <hr style="border:none;border-top:1px solid #d8cfc4;margin:18px 0;">

              <p style="color:#6b6358;font-size:12px;">
                Reply directly to this email — it goes to {safeName}.
              </p>
            </div>
            """;

        return new EmailMessage(
            ToEmail:      _orders.RecipientEmail,
            ToName:       _orders.RecipientName,
            FromEmail:    _contact.SenderEmail,
            FromName:     _contact.SenderBrand,
            Subject:      subject,
            HtmlBody:     html,
            TextBody:     sb.ToString(),
            ReplyToEmail: customerEmail,
            ReplyToName:  customerName);
    }

    // ── Helpers ────────────────────────────────────────────────────

    // Brackets removed from the subject deliberately; some spam filters
    // score "[Brand]" prefixes. The brand still appears in the From
    // display name, so Noemi's inbox shows it without the subject paying
    // a filter penalty.
    private static string BuildSubject(OrderDto order)
    {
        var total = $"${order.AmountCents / 100m:0.00}";
        return $"Linen Lady — Order #{order.OrderId} paid ({total})";
    }

    private (string html, string text) RenderBody(
        OrderDto order, string customerName, string customerEmail)
    {
        // ── Plaintext ───────────────────────────────────────────────
        var sb = new StringBuilder();
        sb.AppendLine($"Order #{order.OrderId} has been paid.");
        sb.AppendLine();
        sb.AppendLine($"Customer: {customerName} <{customerEmail}>");
        sb.AppendLine($"Total:    ${order.AmountCents / 100m:0.00}");
        if (order.PaidAt is not null)
            sb.AppendLine($"Paid at:  {order.PaidAt.Value:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();
        sb.AppendLine("Items:");
        foreach (var i in order.Items)
        {
            var sku = string.IsNullOrWhiteSpace(i.ItemSku) ? "" : $" (SKU {i.ItemSku})";
            sb.AppendLine($"  • {i.ItemName}{sku} — ${i.UnitPriceCents / 100m:0.00}");
        }
        sb.AppendLine();
        sb.AppendLine("Ship to:");
        sb.AppendLine($"  {order.ShipLabel}");
        sb.AppendLine($"  {order.ShipStreet1}");
        if (!string.IsNullOrWhiteSpace(order.ShipStreet2))
            sb.AppendLine($"  {order.ShipStreet2}");
        sb.AppendLine($"  {order.ShipCity}, {order.ShipState} {order.ShipZip}");
        if (!string.Equals(order.ShipCountry, "US", StringComparison.OrdinalIgnoreCase))
            sb.AppendLine($"  {order.ShipCountry}");

        if (!string.IsNullOrWhiteSpace(order.CustomerNotes))
        {
            sb.AppendLine();
            sb.AppendLine("Customer note:");
            sb.AppendLine(order.CustomerNotes);
        }

        if (!string.IsNullOrWhiteSpace(_orders.StorefrontOrigin))
        {
            sb.AppendLine();
            sb.AppendLine($"View in admin: {_orders.StorefrontOrigin.TrimEnd('/')}/admin/orders/{order.OrderId}");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine($"Reply directly to this email — it goes to {customerName}.");

        // ── HTML ────────────────────────────────────────────────────
        // Same visual language as the Contact owner-email (Georgia,
        // muted palette) so Noemi's inbox sees a consistent house style.
        var safeName  = WebUtility.HtmlEncode(customerName);
        var safeMail  = WebUtility.HtmlEncode(customerEmail);
        var safeNotes = string.IsNullOrWhiteSpace(order.CustomerNotes)
            ? null
            : WebUtility.HtmlEncode(order.CustomerNotes).Replace("\n", "<br>");

        var itemsHtml = string.Join("", order.Items.Select(i =>
        {
            var skuHtml = string.IsNullOrWhiteSpace(i.ItemSku)
                ? ""
                : $" <span style='color:#6b6358;'>(SKU {WebUtility.HtmlEncode(i.ItemSku)})</span>";
            return $"<li>{WebUtility.HtmlEncode(i.ItemName)}{skuHtml} — <strong>${i.UnitPriceCents / 100m:0.00}</strong></li>";
        }));

        var addrHtml = new StringBuilder();
        addrHtml.Append(WebUtility.HtmlEncode(order.ShipLabel ?? "")).Append("<br>");
        addrHtml.Append(WebUtility.HtmlEncode(order.ShipStreet1 ?? "")).Append("<br>");
        if (!string.IsNullOrWhiteSpace(order.ShipStreet2))
            addrHtml.Append(WebUtility.HtmlEncode(order.ShipStreet2)).Append("<br>");
        addrHtml.Append(WebUtility.HtmlEncode(order.ShipCity ?? "")).Append(", ")
                .Append(WebUtility.HtmlEncode(order.ShipState ?? "")).Append(' ')
                .Append(WebUtility.HtmlEncode(order.ShipZip ?? ""));
        if (!string.Equals(order.ShipCountry, "US", StringComparison.OrdinalIgnoreCase))
            addrHtml.Append("<br>").Append(WebUtility.HtmlEncode(order.ShipCountry ?? ""));

        var notesBlock = safeNotes is null
            ? ""
            : $"""
              <p style="color:#6b6358;font-size:13px;margin-top:18px;margin-bottom:4px;">Customer note</p>
              <p style="white-space:pre-wrap;line-height:1.55;background:#faf6f0;padding:10px 12px;border-left:2px solid #d8cfc4;margin:0;">{safeNotes}</p>
              """;

        var adminLinkBlock = string.IsNullOrWhiteSpace(_orders.StorefrontOrigin)
            ? ""
            : $"""
              <p style="margin-top:18px;"><a href="{_orders.StorefrontOrigin.TrimEnd('/')}/admin/orders/{order.OrderId}" style="color:#9b2c3d;">View in admin →</a></p>
              """;

        var html = $"""
            <div style="font-family:Georgia,serif;max-width:560px;color:#3a342e;">
              <p style="color:#6b6358;font-size:13px;margin-bottom:4px;">
                Order <strong>#{order.OrderId}</strong> has been paid.
              </p>
              <p style="color:#6b6358;font-size:13px;margin-top:0;">
                From {safeName} &lt;{safeMail}&gt;
              </p>

              <hr style="border:none;border-top:1px solid #d8cfc4;margin:12px 0;">

              <p style="color:#6b6358;font-size:13px;margin-bottom:4px;">Items</p>
              <ul style="margin:0;padding-left:18px;line-height:1.6;">{itemsHtml}</ul>

              <p style="margin-top:14px;font-size:15px;">
                <strong>Total: ${order.AmountCents / 100m:0.00}</strong>
              </p>

              <p style="color:#6b6358;font-size:13px;margin-top:18px;margin-bottom:4px;">Ship to</p>
              <p style="line-height:1.5;margin:0;">{addrHtml}</p>

              {notesBlock}

              <hr style="border:none;border-top:1px solid #d8cfc4;margin:18px 0;">

              <p style="color:#6b6358;font-size:12px;">
                Reply directly to this email — it goes to {safeName}.
              </p>

              {adminLinkBlock}
            </div>
            """;

        return (html, sb.ToString());
    }
}