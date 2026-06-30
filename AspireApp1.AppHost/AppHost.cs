var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.AspireApp1_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var apiService2 = builder.AddProject<Projects.AspireApp1_ApiService2>("apiservice2")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);
// Add reference to apiService2, so apiService can call it
apiService.WithReference(apiService2);

var apiServiceExternal = builder.AddProject<Projects.AspireApp1_ApiExternalService>("apiexternalservice")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(apiService2)
    .WaitFor(apiService2);
// Add reference to apiServiceExternal, so apiService2 can call them
apiService2.WithReference(apiServiceExternal);

var apiServiceStaticWeather = builder.AddProject<Projects.AspireApp1_ApiServiceStaticWeather>("apiservicestaticweather")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(apiService2)
    .WaitFor(apiService2);
// Add reference to apiServiceStaticWeather, so apiService2 can call them
apiService2.WithReference(apiServiceStaticWeather);

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
    .WithReference(apiService2)
    .WithReference(apiServiceStaticWeather);

builder.AddProject<Projects.AspireApp1_WorkerService1>("workerservice1");

builder.Build().Run();
