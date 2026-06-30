var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.AspireApp1_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var apiService2 = builder.AddProject<Projects.AspireApp1_ApiService2>("apiservice2")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(apiService)
    .WaitFor(apiService);

var apiService3 = builder.AddProject<Projects.AspireApp1_ApiService3>("apiservice3")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithReference(apiService2)
    .WaitFor(apiService2);

// Add reference to apiService2 so apiService can call it
apiService.WithReference(apiService2);
// Add reference to apiService3 so apiService2 can call it
apiService2.WithReference(apiService3);

builder.AddProject<Projects.AspireApp1_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService)
    .WithReference(apiService2)
    .WithReference(apiService3);

builder.AddProject<Projects.AspireApp1_WorkerService1>("workerservice1");

builder.Build().Run();
