using Messages;
using Microsoft.Extensions.Hosting;
using Raffle;
using System.Text.Json;

Console.Title = "Raffle";

var attendees = await File.ReadAllLinesAsync("C:\\Particular\\BoothDemo\\raffle.txt");

foreach (var name in attendees)
{
    Console.WriteLine(name);
}

RaffleOrderCompletedHandler.Names = attendees;

Console.WriteLine();
Console.WriteLine("Stating the raffle endpoint...");

var builder = Host.CreateApplicationBuilder(args);

var endpointConfiguration = PrepareEndpointConfiguration();

builder.Services.AddNServiceBusEndpoint(endpointConfiguration);
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

    var azureConnectionString = Environment.GetEnvironmentVariable("AzureServiceBus_Techorama_ConnectionString")!;
    var transport = new AzureServiceBusTransport(azureConnectionString, TopicTopology.Default)
    {
        TransportTransactionMode = TransportTransactionMode.ReceiveOnly
    };
    cfg.Recoverability().Immediate(x => x.NumberOfRetries(0));
    cfg.Recoverability().Delayed(x => x.NumberOfRetries(0));
    cfg.AuditProcessedMessagesTo("audit");
    cfg.SendFailedMessagesTo("error");
    cfg.UseTransport(transport);
    cfg.UsePersistence<NonDurablePersistence>();
    cfg.EnableInstallers();

    return cfg;
}