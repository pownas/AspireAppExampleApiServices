using AspireApp1.StateStore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace AspireApp1.Tests;

[TestClass]
public class StateStoreDbRegistrationTests
{
    [TestMethod]
    public void ResolveProvider_DefaultsToSqlite()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var provider = StateStoreDbRegistration.ResolveProvider(configuration);

        Assert.AreEqual("sqlite", provider);
    }

    [TestMethod]
    public void ResolveConnectionString_UsesSqlServerConnectionWhenConfigured()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StateStore:Provider"] = "SqlServer",
                ["ConnectionStrings:statestoreSqlServer"] = "Server=.;Database=AspireApp1StateStore;Trusted_Connection=True;TrustServerCertificate=True;"
            })
            .Build();

        var provider = StateStoreDbRegistration.ResolveProvider(configuration);
        var connectionString = StateStoreDbRegistration.ResolveConnectionString(configuration, provider);

        StringAssert.Contains(connectionString, "Server=.");
        StringAssert.Contains(connectionString, "Database=AspireApp1StateStore");
    }

    [TestMethod]
    public void ResolveProvider_UsesSqliteWhenConfigured()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StateStore:Provider"] = "Sqlite"
            })
            .Build();

        var provider = StateStoreDbRegistration.ResolveProvider(configuration);

        Assert.AreEqual("sqlite", provider);
    }

    [TestMethod]
    public async Task AddConfiguredStateStoreDbContext_WithSqlitePath_CreatesDatabaseFile()
    {
        var dbDir = Path.Combine(Path.GetTempPath(), "AspireApp1Tests", Guid.NewGuid().ToString("N"));
        var dbPath = Path.Combine(dbDir, "statestore.db");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StateStore:Provider"] = "Sqlite",
                ["ConnectionStrings:statestore"] = $"Data Source={dbPath}"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddConfiguredStateStoreDbContext(configuration);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StateStoreDbContext>();

        await DatabaseInitializer.EnsureSchemaAsync(db);
        await DatabaseInitializer.EnsureSchemaAsync(db);

        Assert.IsTrue(File.Exists(dbPath));
    }
}
