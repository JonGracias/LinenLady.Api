// Features/Customers/Services/ExpireStaleOrdersBackgroundService.cs
//
// Sweeper for orders stuck in PaymentPending past the configured timeout
// (default 24h, matching Square's payment-link expiry). On each tick:
//   1. Find PaymentPending orders older than the timeout.
//   2. Cancel each — flips status to 'Cancelled', stamps CancelledAt.
//   3. For each cancelled order, recreate Active reservations for items
//      that are still purchasable so they return to the customer's basket.
//
// Mirrors the existing ExpireReservationsBackgroundService pattern — same
// scope/run-every-minute style, same scoped-handler resolution per tick.
// Both sweepers run independently because they handle different state
// transitions and could legitimately fail in isolation.

namespace LinenLady.API.Customers.Services;

using LinenLady.API.Customers.Handler;

public sealed class ExpireStaleOrdersBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExpireStaleOrdersBackgroundService> _log;

    // Run interval. One minute is overkill for a sweeper that fires on a
    // 24h horizon, but keeps the operation cheap and lets a shortened
    // dev/QA timeout (e.g. 1h) actually flush in a reasonable wall-clock
    // window. Pulled from config so it's tuneable without redeploying.
    private readonly TimeSpan _interval;

    public ExpireStaleOrdersBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<ExpireStaleOrdersBackgroundService> log)
    {
        _scopeFactory = scopeFactory;
        _log          = log;
        _interval     = TimeSpan.FromMinutes(
            config.GetValue<int?>("Checkout:OrderSweepIntervalMinutes") ?? 1);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation(
            "ExpireStaleOrdersBackgroundService started (interval {Interval}).",
            _interval);

        // Initial small delay so the host isn't doing cleanup work mid-startup.
        try { await Task.Delay(TimeSpan.FromSeconds(30), ct); }
        catch (TaskCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var handler = scope.ServiceProvider
                    .GetRequiredService<ExpireStaleOrdersHandler>();

                await handler.HandleAsync(ct);
            }
            catch (Exception ex)
            {
                // Never let a sweep failure kill the background service —
                // log and try again on the next tick. The same defensive
                // pattern as the reservation sweeper.
                _log.LogError(ex, "Order sweep failed; will retry next tick.");
            }

            try { await Task.Delay(_interval, ct); }
            catch (TaskCanceledException) { return; }
        }
    }
}
