var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.AspireApp1_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

var apiService2 = builder.AddProject<Projects.AspireApp1_ApiService2>("apiservice2")
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

var apiService3 = builder.AddProject<Projects.AspireApp1_ApiService3>("apiservice3")
    .WithHttpHealthCheck("/health")
    .WithReference(apiService2)
    .WaitFor(apiService2);

builder.AddProject<Projects.AspireApp1_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.AddProject<Projects.AspireApp1_WorkerService1>("workerservice1");

builder.Build().Run();
