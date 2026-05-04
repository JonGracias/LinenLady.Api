// Features/Customers/Handler/SquareWebhookHandler.cs (REPLACES the old version)
//
// Square posts to /api/square/webhook on payment events. Reference-id
// routing now distinguishes legacy single-item reservation links
// ("RES-{id}") from new multi-item order links ("ORD-{id}"). The
// reservation path is kept only for in-flight pre-migration links;
// after their 24h timeout passes we can delete it.
//
// Severe #3 (signature verification) tracking note carries over from
// the old handler — the endpoint is [AllowAnonymous] and we still need
// to validate Square-Signature against the webhook secret before
// trusting the body. Marked TODO inline so it's not lost.

namespace LinenLady.API.Customers.Handler;

using LinenLady.API.Customers.Sql;

public sealed class SquareWebhookHandler
{
    private readonly ICustomerRepository _repo;
    private readonly ILogger<SquareWebhookHandler> _log;

    public SquareWebhookHandler(
        ICustomerRepository repo,
        ILogger<SquareWebhookHandler> log)
    {
        _repo = repo;
        _log  = log;
    }

    public async Task HandleAsync(string rawBody, CancellationToken ct)
    {
        // TODO (Severe #3): verify Square-Signature header here against the
        // webhook signing key before doing anything with rawBody. Until that
        // lands, anyone can POST to /api/square/webhook and forge a Paid
        // event. Currently mitigated only by Square's reference_id being
        // non-guessable for an attacker who doesn't know the order id.

        using var doc = System.Text.Json.JsonDocument.Parse(rawBody);
        var root = doc.RootElement;
        var eventType = root.GetProperty("type").GetString();

        if (eventType != "payment.completed" && eventType != "order.fulfillment.updated")
            return;

        var referenceId = root
            .GetProperty("data").GetProperty("object")
            .GetProperty("order").GetProperty("reference_id")
            .GetString();

        if (string.IsNullOrEmpty(referenceId)) return;

        // Order path — new multi-item flow.
        if (referenceId.StartsWith("ORD-"))
        {
            // Square's order.id (UUID) is the lookup key — we stored it as
            // SquareOrderId on cust.[Order] when the link was created. The
            // ORD-{n} reference_id is informational only.
            var squareOrderId = root
                .GetProperty("data").GetProperty("object")
                .GetProperty("order").GetProperty("id")
                .GetString();

            if (string.IsNullOrEmpty(squareOrderId)) return;

            var paid = await _repo.MarkOrderPaidAsync(squareOrderId);
            if (paid is null)
            {
                _log.LogWarning(
                    "Webhook for unknown Square order {SquareOrderId} (ref {Ref}).",
                    squareOrderId, referenceId);
                return;
            }

            await _repo.LogNotificationAsync(
                paid.CustomerId, reservationId: null, "PaymentReceived", true);

            _log.LogInformation(
                "Order {OrderId} marked Paid via Square webhook ({ItemCount} items).",
                paid.OrderId, paid.Items.Count);
            return;
        }

        // Legacy path — single-item reservations from before the basket
        // migration. Drop this branch after the migration's pre-existing
        // payment links have all expired (Square's default is 24h).
        if (referenceId.StartsWith("RES-")
            && int.TryParse(referenceId[4..], out var reservationId))
        {
            // The legacy UpdateReservationStatusAsync method is gone in the
            // new schema (status collapsed). Pre-migration links pointing
            // at RES-{id} reservation rows now resolve to rows with
            // Status='Expired' (per the backfill), so there's nothing
            // useful to do but log and move on. If we ever discover a
            // pre-migration payment link gets paid post-migration, we'd
            // need to manually create a synthetic Order — but that's a
            // ops concern, not a code concern.
            _log.LogWarning(
                "Webhook for legacy reservation {ResId} — pre-migration link, " +
                "no automatic action. Manual reconciliation may be required.",
                reservationId);
        }
    }
}
