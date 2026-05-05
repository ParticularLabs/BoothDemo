using Messages;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

Console.Title = "Raffle";

var builder = Host.CreateApplicationBuilder(args);

var endpointConfiguration = PrepareEndpointConfiguration();

builder.UseNServiceBus(endpointConfiguration);
await builder.Build().RunAsync();

EndpointConfiguration PrepareEndpointConfiguration()
{
    var cfg = new EndpointConfiguration("Raffle");
    cfg.LimitMessageProcessingConcurrencyTo(1);

    var serializer = cfg.UseSerialization<SystemJsonSerializer>();
    serializer.Options(new JsonSerializerOptions
    {
        TypeInfoResolverChain =
        {
            MessagesSerializationContext.Default
        }
    });
    cfg.CustomDiagnosticsWriter((diagnostics, ct) => Task.CompletedTask);

    var azureConnectionString = Environment.GetEnvironmentVariable("AzureServiceBus_ConnectionString")!;
    var transport = new AzureServiceBusTransport(azureConnectionString, TopicTopology.Default)
    {
        TransportTransactionMode = TransportTransactionMode.ReceiveOnly
    };
    cfg.UseTransport(transport);
    cfg.UsePersistence<NonDurablePersistence>();
    cfg.EnableInstallers();

    return cfg;
}