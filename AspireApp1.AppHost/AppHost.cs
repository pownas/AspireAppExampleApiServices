var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.AspireApp1_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var apiServiceForecast = builder.AddProject<Projects.AspireApp1_ApiServiceForecast>("apiserviceforecast")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);
// Add reference to apiServiceForecast, so apiService can call it
apiService.WithReference(apiServiceForecast);

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

builder.AddProject<Projects.AspireApp1_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithReference(apiServiceForecast)
    .WithReference(apiServiceStaticWeather);

var workerService = builder.AddProject<Projects.AspireApp1_WorkerService1>("workerservice1")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

apiServiceForecast.WithReference(workerService).WaitFor(workerService);

builder.Build().Run();
