var builder = DistributedApplication.CreateBuilder(args);

// PostgreSQL state store — shared by all worker services
var postgres = builder.AddPostgres("postgres").WithDataVolume();
var stateDb = postgres.AddDatabase("statestore");

var apiService = builder.AddProject<Projects.AspireApp1_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var apiErrorService = builder.AddProject<Projects.AspireApp1_ApiErrorService>("apierrorservice")
    .WithExternalHttpEndpoints();

var apiServiceForecast = builder.AddProject<Projects.AspireApp1_ApiServiceForecast>("apiserviceforecast")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);
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

builder.AddProject<Projects.AspireApp1_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithReference(apiServiceForecast)
    .WithReference(apiServiceStaticWeather);

var workerService1 = builder.AddProject<Projects.AspireApp1_WorkerService1>("workerservice1")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(apiServiceStaticWeather)
    .WithReference(stateDb)
    .WaitFor(stateDb);

apiServiceForecast.WithReference(workerService1).WaitFor(workerService1);

var workerService2 = builder.AddProject<Projects.AspireApp1_WorkerService2>("workerservice2")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(apiServiceStaticWeather)
    .WithReference(stateDb)
    .WaitFor(stateDb);

workerService1.WithReference(workerService2).WaitFor(workerService2);

var workerService3 = builder.AddProject<Projects.AspireApp1_WorkerService3>("workerservice3")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(apiServiceStaticWeather)
    .WithReference(stateDb)
    .WaitFor(stateDb);

workerService1.WithReference(workerService3).WaitFor(workerService3);
workerService2.WithReference(workerService3).WaitFor(workerService3);

var workerService4 = builder.AddProject<Projects.AspireApp1_WorkerService4>("workerservice4")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(workerService1)
    .WithReference(workerService2)
    .WithReference(workerService3)
    .WithReference(stateDb)
    .WaitFor(workerService1)
    .WaitFor(workerService2)
    .WaitFor(workerService3)
    .WaitFor(stateDb);

builder.Build().Run();
