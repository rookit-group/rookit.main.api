using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace MainHub.Api.IntegrationTests;

// Spins up a real Postgres 16 container once for the whole test run (see
// PostgresCollection below). Each test class in the collection gets the same
// fixture instance, and each test resets state by calling ResetAsync() which
// TRUNCATEs every table. This is the alternative to mocking Npgsql - we
// exercise real SQL against a real database, so a typo in a column name or a
// missing parameter fails a test instead of silently drifting until prod.
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("mainhub_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        DataSource = new NpgsqlDataSourceBuilder(_container.GetConnectionString()).Build();

        // Run the same init.sql the docker-compose postgres container runs on
        // first start - copied into the test output via <Content Include=...>.
        var initSqlPath = Path.Combine(AppContext.BaseDirectory, "db", "init.sql");
        var initSql = await File.ReadAllTextAsync(initSqlPath);

        await using var cmd = DataSource.CreateCommand(initSql);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await DataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    // Called at the start of each test to give it a clean slate. CASCADE is
    // needed because of the FK relationships in init.sql - truncating users
    // without CASCADE would fail while there are refresh_tokens/vehicles rows.
    public async Task ResetAsync()
    {
        const string sql = @"
            TRUNCATE TABLE
                refresh_tokens,
                service_history_records,
                service_histories,
                vehicles,
                users
            RESTART IDENTITY CASCADE";
        await using var cmd = DataSource.CreateCommand(sql);
        await cmd.ExecuteNonQueryAsync();
    }
}

// xUnit's collection-fixture mechanism: every test class marked
// [Collection("Postgres")] gets the same PostgresFixture instance, which means
// one container startup for the whole test run instead of one per test class.
[CollectionDefinition("Postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture> { }
