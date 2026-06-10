// Features/Square/SquareService.paymentlink.cs
//
// Payment-link deletion. Called best-effort AFTER an order has already been
// cancelled in the database — never the other way around. The DB is the
// source of truth for order state; this call just revokes the stale Square
// checkout URL so a customer can't pay a cancelled order from an old tab
// or email.
//
// Contract:
//   • 200            → link deleted. Done.
//   • 404            → link already gone (deleted earlier, or never created).
//                      Treated as success — the goal state is "no live link",
//                      and that's the state we're in.
//   • anything else  → throws InvalidOperationException with the Square
//                      error body. CALLERS MUST CATCH AND LOG, NOT RETHROW —
//                      a Square outage must never fail a cancellation that
//                      has already committed locally.

namespace LinenLady.API.Square;

using System.Net;

public partial interface ISquareService
{
    /// <summary>
    /// Deletes a Square payment link so its checkout URL stops accepting
    /// payment. Idempotent: a 404 (already deleted / never existed) is
    /// treated as success. Throws on any other non-success response.
    /// </summary>
    Task DeletePaymentLinkAsync(string paymentLinkId, CancellationToken ct = default);
}

public partial class SquareService : ISquareService
{
    public async Task DeletePaymentLinkAsync(
        string paymentLinkId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(paymentLinkId))
            return; // nothing to delete — order never got a link

        var response = await _http.DeleteAsync(
            $"v2/online-checkout/payment-links/{Uri.EscapeDataString(paymentLinkId)}", ct);

        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
            return;

        var err = await response.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException(
            $"Square payment-link delete failed for {paymentLinkId}: " +
            $"{(int)response.StatusCode}: {err}");
    }
}
