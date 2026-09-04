// Aspire AppHost — orchestrates the Engineering Council POC.
// v1 hosts a single service (the API). Future council services (additional
// analyzer agents, an Ollama sidecar, a dashboard front-end) are added here.

var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.EngineeringCouncil_Api>("api")
    .WithEnvironment("OUTPUTS_ROOT", "outputs");

builder.Build().Run();
