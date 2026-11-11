using Npgsql;
using TutorManagement.Infrastructure.Persistence;

namespace TutorManagement.IntegrationTests;

public sealed class DatabaseConnectionPoolTests
{
    private const string Plain = "Host=localhost;Port=5432;Database=tutor_management;Username=tutor_app";

    [Fact]
    public void Tune_keeps_connections_warm_by_default()
    {
        var tuned = new NpgsqlConnectionStringBuilder(DatabaseConnectionPool.Tune(Plain));

        Assert.Equal(DatabaseConnectionPool.DefaultMinPoolSize, tuned.MinPoolSize);
        Assert.Equal(DatabaseConnectionPool.DefaultIdleLifetimeSeconds, tuned.ConnectionIdleLifetime);
        Assert.Equal("tutor_management", tuned.Database);
    }

    [Fact]
    public void Tune_respects_values_set_in_the_connection_string()
    {
        var tuned = new NpgsqlConnectionStringBuilder(
            DatabaseConnectionPool.Tune(Plain + ";Minimum Pool Size=0;Connection Idle Lifetime=120"));

        Assert.Equal(0, tuned.MinPoolSize);
        Assert.Equal(120, tuned.ConnectionIdleLifetime);
    }

    [Fact]
    public void Tune_leaves_unpooled_connection_strings_alone()
    {
        var unpooled = Plain + ";Pooling=false";

        Assert.Equal(unpooled, DatabaseConnectionPool.Tune(unpooled));
    }

    [Fact]
    public void Tune_never_exceeds_the_maximum_pool_size()
    {
        var tuned = new NpgsqlConnectionStringBuilder(DatabaseConnectionPool.Tune(Plain + ";Maximum Pool Size=4"));

        Assert.Equal(4, tuned.MinPoolSize);
    }

    [Fact]
    public async Task Warm_up_reports_failures_instead_of_throwing()
    {
        var opened = await DatabaseConnectionPool.WarmUpAsync(
            "Host=127.0.0.1;Port=1;Database=none;Username=none;Timeout=1;Pooling=false", 2, CancellationToken.None);

        Assert.Equal(0, opened);
    }
}
