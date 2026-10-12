using Microsoft.EntityFrameworkCore;
using Orbital.Data;
using Orbital.Models;

namespace Orbital.Service;

public class StaleCallCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StaleCallCleanupService> _logger;

    public StaleCallCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<StaleCallCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));

        while (!ct.IsCancellationRequested && await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var now = DateTime.UtcNow;
                var ringingCutoff = now.AddMinutes(-2);
                var activeCutoff = now.AddHours(-4);

                var affected = await db.CallSessions
                    .Where(c => c.EndedAt == null &&
                        ((c.Status == CallStatus.Ringing && c.StartedAt < ringingCutoff) ||
                         (c.Status == CallStatus.Active && c.StartedAt < activeCutoff)))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.EndedAt, now)
                        .SetProperty(c => c.Status, CallStatus.Ended), ct);

                if (affected > 0)
                {
                    _logger.LogInformation("Swept {Count} stale call session(s).", affected);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stale call session sweep failed.");
            }
        }
    }
}