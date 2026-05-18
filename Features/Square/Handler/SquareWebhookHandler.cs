// Features/Customers/Handler/SquareWebhookHandler.cs
//
// Square posts to /api/square/webhook on payment events. Signature
// verification is handled upstream in SquareWebhookController before
// this handler is reached (Severe #3 — resolved).
//
// This handler processes payment.updated events. Those carry the
// Square order_id directly on the payment object, but NOT our
// reference_id — so routing is by SquareOrderId lookup alone. The
// legacy RES-{id} reference-id path is gone; pre-migration reservation
// links have all passed their 24h expiry.
//
// ── Side effects on a confirmed payment ─────────────────────────────
//
//   1. cust.[Order].Status = 'Paid' (via MarkOrderPaidAsync — also
//      flips the line-item inventory rows to IsActive = 0).
//   2. A customer-side notification row in cust.Notification.
//   3. An order-paid email to Noemi (this file), guarded by an atomic
//      sentinel on the order row so Square's retry/duplicate delivery
//      can't produce a second email.

namespace LinenLady.API.Square.Handler;

using System.Text.Json;
using LinenLady.API.Contracts;
using LinenLady.API.Customers.Sql;
using LinenLady.API.Features.Email;
using LinenLady.API.Features.Orders.Email;

public sealed class SquareWebhookHandler
{
    private readonly ICustomerRepository       _repo;
    private readonly IEmailSender              _emailSender;
    private readonly OrderPaidEmailComposer    _emailComposer;
    private readonly ILogger<SquareWebhookHandler> _log;

    public SquareWebhookHandler(
        ICustomerRepository       repo,
        IEmailSender              emailSender,
        OrderPaidEmailComposer    emailComposer,
        ILogger<SquareWebhookHandler> log)
    {
        _repo          = repo;
        _emailSender   = emailSender;
        _emailComposer = emailComposer;
        _log           = log;
    }

    public async Task HandleAsync(string rawBody, CancellationToken ct)
    {
        _log.LogInformation("Square Webhook Received: {Body}", rawBody);

        using var doc = JsonDocument.Parse(rawBody);
        var root = doc.RootElement;

        // ── Event type ───────────────────────────────────────────────
        if (!root.TryGetProperty("type", out var typeEl))
        {
            _log.LogWarning("Webhook payload missing 'type' — ignoring.");
            return;
        }

        var eventType = typeEl.GetString();
        if (eventType != "payment.updated")
        {
            _log.LogDebug("Ignoring non-payment.updated event: {Type}", eventType);
            return;
        }

        // ── Navigate to the payment object ───────────────────────────
        if (!root.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("object", out var obj) ||
            !obj.TryGetProperty("payment", out var payment))
        {
            _log.LogWarning(
                "payment.updated missing data.object.payment — ignoring.");
            return;
        }

        // ── Status gate ──────────────────────────────────────────────
        if (!payment.TryGetProperty("status", out var statusEl))
        {
            _log.LogWarning("payment.updated missing payment.status — ignoring.");
            return;
        }

        var status = statusEl.GetString();
        if (status != "COMPLETED")
        {
            _log.LogDebug(
                "payment.updated with non-terminal status {Status} — ignoring.",
                status);
            return;
        }

        // ── Order id ─────────────────────────────────────────────────
        if (!payment.TryGetProperty("order_id", out var orderIdEl))
        {
            _log.LogWarning(
                "Completed payment with no order_id — ignoring. " +
                "Cannot map to a cust.[Order] row.");
            return;
        }

        var squareOrderId = orderIdEl.GetString();
        if (string.IsNullOrEmpty(squareOrderId))
        {
            _log.LogWarning("Completed payment had empty order_id — ignoring.");
            return;
        }

        // ── Mark paid ────────────────────────────────────────────────
        // MarkOrderPaidAsync is idempotent: only the first call for a
        // given order actually flips Status. Subsequent calls re-read
        // the row and return it as-is. We can't tell which case happened
        // from the return value alone — that's what TryClaimOrderPaidEmailAsync
        // is for below.
        var paid = await _repo.MarkOrderPaidAsync(squareOrderId);
        if (paid is null)
        {
            _log.LogWarning(
                "payment.updated COMPLETED for unknown Square order {SquareOrderId}.",
                squareOrderId);
            return;
        }

        await _repo.LogNotificationAsync(
            paid.CustomerId, reservationId: null, "PaymentReceived", true);

        _log.LogInformation(
            "Order {OrderId} marked Paid via Square webhook ({ItemCount} items).",
            paid.OrderId, paid.Items.Count);

        // ── Order-paid email (at-most-once) ──────────────────────────
        // Sentinel-guarded. The UPDATE inside TryClaimOrderPaidEmailAsync
        // returns true only for the caller that flipped
        // NotificationEmailSentAt from NULL to now. Square retries and
        // duplicate deliveries get false and skip sending — durably,
        // across process restarts.
        await TrySendOrderPaidEmailAsync(paid, ct);
    }

    private async Task TrySendOrderPaidEmailAsync(
        OrderDto paid, CancellationToken ct)
    {
        // Outer try/catch is narrow on purpose: we only swallow expected
        // email-transport failures, so the webhook can still 200 to
        // Square. Anything else (NRE on a malformed row, Dapper
        // transient on the customer-lookup query) is a bug — let it
        // bubble out to the controller's exception handler.
        bool claimed;
        try
        {
            claimed = await _repo.TryClaimOrderPaidEmailAsync(paid.OrderId);
        }
        catch (Exception ex)
        {
            // Sentinel claim failure is unusual enough to surface, but
            // we don't want it to fail the webhook — Square would retry
            // and re-mark-paid (a no-op) and re-claim (would succeed if
            // the original claim genuinely failed). Log and move on.
            _log.LogError(ex,
                "Order-paid email claim failed for order {OrderId}; not sending.",
                paid.OrderId);
            return;
        }

        if (!claimed)
        {
            _log.LogDebug(
                "Order-paid email for order {OrderId} already claimed — skipping.",
                paid.OrderId);
            return;
        }

        // We now own the responsibility to send. If the actual send
        // fails, the sentinel already says "sent" — so we lose the
        // email on a transient Resend outage. The alternative (unset
        // the sentinel on failure) opens a window where two parallel
        // calls could both claim. Given Square's retry cadence is
        // measured in minutes and Resend outages are rare, accepting
        // the loss here is the right tradeoff. If this ever bites, the
        // failure shows up in the warning log below.

        // Customer lookup for the email's "from" line + reply-to. Could
        // be denormalized onto OrderDto later if this becomes the only
        // reason for a second query; for now the cost is one cached read.
        var customer = await _repo.GetByIdAsync(paid.CustomerId);
        if (customer is null)
        {
            _log.LogWarning(
                "Order-paid email skipped for order {OrderId}: customer {CustomerId} not found.",
                paid.OrderId, paid.CustomerId);
            return;
        }

        var customerName = string.Join(" ",
            new[] { customer.FirstName, customer.LastName }
                .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        if (string.IsNullOrWhiteSpace(customerName))
            customerName = customer.Email;

        try
        {
            var message = _emailComposer.Build(paid, customer.Email, customerName);
            var providerId = await _emailSender.SendAsync(message, ct);

            _log.LogInformation(
                "Order-paid email sent for order {OrderId} (provider id {ProviderId}).",
                paid.OrderId, providerId);
        }
        catch (EmailSendException ex)
        {
            _log.LogWarning(ex,
                "Order-paid email send failed for order {OrderId} (non-fatal — order still marked Paid).",
                paid.OrderId);
        }
    }
}