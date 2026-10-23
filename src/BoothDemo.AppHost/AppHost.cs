using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

// Nothing is launched from here. Every part of the booth demo runs elsewhere (Raspberry Pis, Azure Container Apps), so the
// AppHost only lists them as external services with health checks and hosts the dashboard that receives their telemetry.
var builder = DistributedApplication.CreateBuilder(args);

foreach (var service in builder.Configuration.GetSection("HttpServices").GetChildren())
{
    var resource = builder.AddExternalService(service.Key, service["Url"]!);

    if (service["HealthPath"] is { } healthPath)
    {
        resource.WithHttpHealthCheck(healthPath);
    }
}

// Services without an HTTP endpoint are checked by opening a TCP connection
foreach (var service in builder.Configuration.GetSection("TcpServices").GetChildren())
{
    var (host, port) = TcpHealthCheck.ParseAddress(service.Value!);
    var healthCheckKey = $"{service.Key}-tcp";

    builder.Services.AddHealthChecks().Add(new HealthCheckRegistration(
        healthCheckKey,
        _ => new TcpHealthCheck(host, port),
        HealthStatus.Unhealthy,
        tags: null));

    builder.AddExternalService(service.Key, $"tcp://{host}:{port}")
        .WithHealthCheck(healthCheckKey);
}

builder.Build().Run();
