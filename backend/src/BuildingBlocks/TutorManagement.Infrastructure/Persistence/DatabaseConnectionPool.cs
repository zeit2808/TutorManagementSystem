using Npgsql;

namespace TutorManagement.Infrastructure.Persistence;

/// <summary>
/// Keeps a few physical Postgres connections open between page loads.
/// With Npgsql's defaults (Minimum Pool Size=0, idle lifetime 300 s) the pool drains after a quiet
/// five minutes, and the next page — which fires ~10 GETs in parallel — has to open that many new
/// connections at once. Each new connection costs 15–130 ms (a new server process plus SCRAM auth),
/// which turned 30 ms requests into 250–300 ms ones.
/// </summary>
public static class DatabaseConnectionPool
{
    public const int DefaultMinPoolSize = 10;
    public const int DefaultIdleLifetimeSeconds = 900;

    /// <summary>
    /// Applies the pool defaults unless the connection string already sets them. All modules must use the
    /// returned string so their DbContexts share one pool (Npgsql keys pools by connection string).
    /// </summary>
    public static string Tune(string connectionString, int minPoolSize = DefaultMinPoolSize,
        int idleLifetimeSeconds = DefaultIdleLifetimeSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (!builder.Pooling)
            return connectionString;

        if (!Mentions(connectionString, "Minimum Pool Size", "MinPoolSize", "Min Pool Size"))
            builder.MinPoolSize = Math.Min(minPoolSize, builder.MaxPoolSize);
        if (!Mentions(connectionString, "Connection Idle Lifetime", "ConnectionIdleLifetime"))
            builder.ConnectionIdleLifetime = idleLifetimeSeconds;
        return builder.ConnectionString;
    }

    /// <summary>
    /// Opens <paramref name="count"/> connections at the same time and returns them to the pool, so the first
    /// page after startup does not pay for opening them. Failures are reported, never thrown: an unreachable
    /// database is /health/ready's job, not a reason to stop the API.
    /// </summary>
    public static async Task<int> WarmUpAsync(string connectionString, int count, CancellationToken cancellationToken)
    {
        if (count <= 0)
            return 0;

        var connections = new NpgsqlConnection[count];
        try
        {
            for (var i = 0; i < count; i++)
                connections[i] = new NpgsqlConnection(connectionString);
            var opened = await Task.WhenAll(connections.Select(async connection =>
            {
                try
                {
                    await connection.OpenAsync(cancellationToken);
                    return true;
                }
                catch (NpgsqlException)
                {
                    return false;
                }
            }));
            return opened.Count(ok => ok);
        }
        finally
        {
            foreach (var connection in connections)
                if (connection is not null)
                    await connection.DisposeAsync();
        }
    }

    private static bool Mentions(string connectionString, params string[] keywords) =>
        connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split('=', 2)[0].Trim())
            .Any(key => keywords.Any(keyword => string.Equals(key, keyword, StringComparison.OrdinalIgnoreCase)));
}
