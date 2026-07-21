using PlanDeck.Application.Planning;

namespace PlanDeck.Server.Realtime;

public sealed class PlanningRoomCleanupService(
    IPlanningRoomService planningRoomService,
    TimeProvider timeProvider,
    ILogger<PlanningRoomCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan InactiveRoomTtl = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(CleanupInterval, timeProvider, stoppingToken);

            var cutoff = timeProvider.GetUtcNow() - InactiveRoomTtl;
            var removedCount = planningRoomService.RemoveInactiveRooms(cutoff);
            if (removedCount > 0)
            {
                logger.LogInformation("Removed {RoomCount} inactive planning rooms.", removedCount);
            }
        }
    }
}
