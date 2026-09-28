using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;

namespace AspireApp1.StateStore;

public static class StateStoreDbRegistration
{
    private const string ProviderConfigKey = "StateStore:Provider";
    private const string SqliteProvider = "sqlite";
    private const string SqlServerProvider = "sqlserver";

    public static IServiceCollection AddConfiguredStateStoreDbContext(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<StateStoreDbContext>(options =>
            ConfigureStateStore(options, configuration));
        return services;
    }

    public static IServiceCollection AddConfiguredStateStoreDbContextFactory(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContextFactory<StateStoreDbContext>(options =>
            ConfigureStateStore(options, configuration));
        return services;
    }

    public static string ResolveProvider(IConfiguration configuration)
    {
        var provider = configuration[ProviderConfigKey]?.Trim();
        if (string.Equals(provider, "SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return SqlServerProvider;
        }

        if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            return SqliteProvider;
        }

        return SqliteProvider;
    }

    public static string ResolveConnectionString(IConfiguration configuration, string provider)
    {
        if (provider == SqlServerProvider)
        {
            return configuration.GetConnectionString("statestoreSqlServer")
                ?? "Server=.;Database=AspireApp1StateStore;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
        }

        return configuration.GetConnectionString("statestore")
            ?? $"Data Source={Path.Combine(Path.GetTempPath(), "AspireApp1StateStore", "statestore.db")}";
    }

    private static void ConfigureStateStore(DbContextOptionsBuilder options, IConfiguration configuration)
    {
        var provider = ResolveProvider(configuration);
        var connectionString = ResolveConnectionString(configuration, provider);

        if (provider == SqlServerProvider)
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure();
                sql.CommandTimeout(30);
            });
        }
        else
        {
            connectionString = NormalizeSqliteConnectionString(connectionString);
            EnsureSqliteDataSourceDirectoryExists(connectionString);
            options.UseSqlite(connectionString);
        }
    }

    private static string NormalizeSqliteConnectionString(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (!string.IsNullOrWhiteSpace(builder.DataSource))
        {
            var dataSource = builder.DataSource.Trim();
            if (Path.IsPathRooted(dataSource) && !OperatingSystem.IsWindows())
            {
                return builder.ToString();
            }

            if (OperatingSystem.IsWindows() && dataSource.StartsWith("/", StringComparison.Ordinal))
            {
                var fileName = Path.GetFileName(dataSource);
                var fallbackDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AspireApp1");
                Directory.CreateDirectory(fallbackDir);
                builder.DataSource = Path.Combine(fallbackDir, string.IsNullOrWhiteSpace(fileName) ? "statestore.db" : fileName);
            }
        }

        return builder.ToString();
    }

    private static void EnsureSqliteDataSourceDirectoryExists(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;
        if (string.IsNullOrWhiteSpace(dataSource))
        {
            return;
        }

        var fullPath = Path.GetFullPath(dataSource);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
