using Microsoft.Extensions.Configuration;
using System.Diagnostics;

EnsureAspireEndpointPortsAreAvailable();

var builder = DistributedApplication.CreateBuilder(args);

var stateStoreProvider = builder.Configuration["StateStore:Provider"] ?? "Sqlite";

// SQLite state store fallback file used only when Provider=Sqlite.
var dbDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "AspireApp1");
Directory.CreateDirectory(dbDir);
var sqliteConnStr = builder.Configuration.GetConnectionString("statestore")
    ?? $"Data Source={Path.Combine(dbDir, "statestore.db")}";
var sqlServerConnStr = builder.Configuration.GetConnectionString("statestoreSqlServer")
    ?? "Server=.;Database=AspireApp1StateStore;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

var apiService = builder.AddProject<Projects.AspireApp1_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var apiErrorService = builder.AddProject<Projects.AspireApp1_ApiErrorService>("apierrorservice")
    .WithExternalHttpEndpoints();

var apiServiceForecast = builder.AddProject<Projects.AspireApp1_ApiServiceForecast>("apiserviceforecast")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithEnvironment("StateStore__Provider", stateStoreProvider)
    .WithEnvironment("ConnectionStrings__statestore", sqliteConnStr)
    .WithEnvironment("ConnectionStrings__statestoreSqlServer", sqlServerConnStr);
// Add reference to apiServiceForecast, so apiService can call it
apiService.WithReference(apiServiceForecast);
// Add reference to apiErrorService, so apiService and apiServiceForecast can call it
apiService.WithReference(apiErrorService);
apiServiceForecast.WithReference(apiErrorService);

var apiServiceExternal = builder.AddProject<Projects.AspireApp1_ApiExternalService>("apiexternalservice")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(apiServiceForecast)
    .WaitFor(apiServiceForecast);
// Add reference to apiServiceExternal, so apiServiceForecast can call them
apiServiceForecast.WithReference(apiServiceExternal);

var apiServiceStaticWeather = builder.AddProject<Projects.AspireApp1_ApiServiceStaticWeather>("apiservicestaticweather")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(apiServiceForecast)
    .WaitFor(apiServiceForecast);
// Add reference to apiServiceStaticWeather, so apiServiceForecast can call them
apiServiceForecast.WithReference(apiServiceStaticWeather);

var apiServicePerson = builder.AddProject<Projects.AspireApp1_ApiServicePerson>("apiserviceperson")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(apiServiceExternal)
    .WithReference(apiServiceStaticWeather)
    .WaitFor(apiServiceExternal)
    .WaitFor(apiServiceStaticWeather);
// Add reference to apiServicePerson, so apiServiceExternal and apiServiceStaticWeather can call it
apiServiceExternal.WithReference(apiServicePerson);
apiServiceStaticWeather.WithReference(apiServicePerson);

var workerService1 = builder.AddProject<Projects.AspireApp1_WorkerService1>("workerservice1")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(apiServiceStaticWeather)
    .WithEnvironment("StateStore__Provider", stateStoreProvider)
    .WithEnvironment("ConnectionStrings__statestore", sqliteConnStr)
    .WithEnvironment("ConnectionStrings__statestoreSqlServer", sqlServerConnStr);

apiServiceForecast.WithReference(workerService1).WaitFor(workerService1);

var workerService2 = builder.AddProject<Projects.AspireApp1_WorkerService2>("workerservice2")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(apiServiceStaticWeather)
    .WithEnvironment("StateStore__Provider", stateStoreProvider)
    .WithEnvironment("ConnectionStrings__statestore", sqliteConnStr)
    .WithEnvironment("ConnectionStrings__statestoreSqlServer", sqlServerConnStr);

workerService1.WithReference(workerService2).WaitFor(workerService2);

var workerService3 = builder.AddProject<Projects.AspireApp1_WorkerService3>("workerservice3")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(apiServiceStaticWeather)
    .WithEnvironment("StateStore__Provider", stateStoreProvider)
    .WithEnvironment("ConnectionStrings__statestore", sqliteConnStr)
    .WithEnvironment("ConnectionStrings__statestoreSqlServer", sqlServerConnStr);

workerService1.WithReference(workerService3).WaitFor(workerService3);
workerService2.WithReference(workerService3).WaitFor(workerService3);

var workerService4 = builder.AddProject<Projects.AspireApp1_WorkerService4>("workerservice4")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WithReference(apiServiceForecast)
    .WithReference(workerService1)
    .WithReference(workerService2)
    .WithReference(workerService3)
    .WithEnvironment("StateStore__Provider", stateStoreProvider)
    .WithEnvironment("ConnectionStrings__statestore", sqliteConnStr)
    .WithEnvironment("ConnectionStrings__statestoreSqlServer", sqlServerConnStr)
    .WaitFor(apiService)
    .WaitFor(apiServiceForecast)
    .WaitFor(workerService1)
    .WaitFor(workerService2)
    .WaitFor(workerService3);

var webFrontend = builder.AddProject<Projects.AspireApp1_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithReference(apiServiceForecast)
    .WithReference(apiServiceStaticWeather)
    .WithEnvironment("StateStore__Provider", stateStoreProvider)
    .WithEnvironment("ConnectionStrings__statestore", sqliteConnStr)
    .WithEnvironment("ConnectionStrings__statestoreSqlServer", sqlServerConnStr);

// The web frontend triggers flows via WorkerService1
webFrontend.WithReference(workerService1);
workerService4.WithReference(webFrontend).WaitFor(webFrontend);

builder.Build().Run();

static void EnsureAspireEndpointPortsAreAvailable()
{
    const string resourceServiceEndpoint = "ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL";
    const string dashboardOtlpEndpoint = "ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL";
    const string dashboardMcpEndpoint = "ASPIRE_DASHBOARD_MCP_ENDPOINT_URL";

    ReleaseConflictingEndpoint(resourceServiceEndpoint);
    ReleaseConflictingEndpoint(dashboardOtlpEndpoint);
    ReleaseConflictingEndpoint(dashboardMcpEndpoint);
}

static void ReleaseConflictingEndpoint(string environmentVariableName)
{
    var endpointValue = Environment.GetEnvironmentVariable(environmentVariableName);
    if (string.IsNullOrWhiteSpace(endpointValue) || !Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpointUri))
    {
        return;
    }

    if (!TryFindListeningProcess(endpointUri.Port, out var processId, out var processName, out var processPath))
    {
        return;
    }

    Console.WriteLine($"[AppHost preflight] Port conflict for {environmentVariableName}={endpointValue}. " +
                      $"Port {endpointUri.Port} is already used by PID {processId} ({processName}) at '{processPath}'. " +
                      $"Clearing {environmentVariableName} so Aspire can choose a free port.");

    Environment.SetEnvironmentVariable(environmentVariableName, null);
}

static bool TryFindListeningProcess(int port, out int processId, out string processName, out string processPath)
{
    processId = 0;
    processName = "unknown";
    processPath = "unknown";

    if (!OperatingSystem.IsWindows())
    {
        return false;
    }

    using var netstatProcess = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "netstat",
            Arguments = "-ano -p tcp",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };

    netstatProcess.Start();
    var output = netstatProcess.StandardOutput.ReadToEnd();
    netstatProcess.WaitForExit();

    foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (!line.StartsWith("TCP", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5 || !parts[3].Equals("LISTENING", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var localAddress = parts[1];
        var separatorIndex = localAddress.LastIndexOf(':');
        if (separatorIndex < 0)
        {
            continue;
        }

        if (!int.TryParse(localAddress[(separatorIndex + 1)..], out var listeningPort) || listeningPort != port)
        {
            continue;
        }

        if (!int.TryParse(parts[4], out processId))
        {
            return false;
        }

        try
        {
            var process = Process.GetProcessById(processId);
            processName = process.ProcessName;
            processPath = process.MainModule?.FileName ?? "unknown";
        }
        catch
        {
            // keep defaults when process metadata is unavailable
        }

        return true;
    }

    return false;
}
