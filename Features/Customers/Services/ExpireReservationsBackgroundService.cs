namespace LinenLady.API.BackgroundServices;

using LinenLady.API.Customers.Handler;

/// <summary>
/// Replaces the hourly TimerTrigger (&quot;0 0 * * * *&quot;) from Azure Functions.
/// Runs on the web host&apos;s lifetime and expires stale reservations every hour
/// on the hour.
/// </summary>
public sealed class ExpireReservationsBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ExpireReservationsBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait until the start of the next hour so runs happen on the hour mark,
        // matching the original cron "0 0 * * * *" schedule.
        await Task.Delay(TimeUntilNextHour(), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<ExpireReservationsHandler>();
                await handler.HandleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let a bad run crash the service — log and keep going
                logger.LogError(ex, "ExpireReservations run failed; will retry next hour.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static TimeSpan TimeUntilNextHour()
    {
        var now = DateTime.UtcNow;
        var nextHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc)
            .AddHours(1);
        return nextHour - now;
    }
}
