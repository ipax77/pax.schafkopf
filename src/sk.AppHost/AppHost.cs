using sk.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.sk_api>("api")
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithHttpEndpointsAsWebSockets();

// var web = builder.AddProject<Projects.sk_pwa>("pwa")
//     .WithExternalHttpEndpoints()
//     .WithReference(api)
//     .WaitFor(api);

builder.Build().Run();
