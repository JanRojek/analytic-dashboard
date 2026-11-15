using AnalyticDashboard.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace AnalyticDashboard.IntegrationTests.Concurrency;

internal static class PostgresLockObserver
{
    public static async Task WaitForBlockerAsync(
        AppDbContext observer,
        int waitingPid,
        int blockerPid,
        CancellationToken cancellationToken)
    {
        await observer.Database.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT @blocker = ANY(pg_blocking_pids(@waiting))",
            (NpgsqlConnection)observer.Database.GetDbConnection()
        );
        command.Parameters.AddWithValue("blocker", blockerPid);
        command.Parameters.AddWithValue("waiting", waitingPid);

        while (!Equals(true, await command.ExecuteScalarAsync(cancellationToken)))
        {
            // The observed PostgreSQL lock releases the barrier; the interval only throttles polling.
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }
}
