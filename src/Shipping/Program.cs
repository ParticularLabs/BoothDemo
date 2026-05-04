using Messages;
using Microsoft.Extensions.Hosting;
using NServiceBus.Transport;
using Shared;
using System.Reflection;
using System.Text.Json;
using NServiceBus.Configuration.AdvancedExtensibility;

var instancePostfix = args.FirstOrDefault();

var title = string.IsNullOrEmpty(instancePostfix) ? "Processing (Shipping)" : $"Shipping - {instancePostfix}";
var instanceName = string.IsNullOrEmpty(instancePostfix) ? "shipping" : $"shipping-{instancePostfix}";
var instanceId = DeterministicGuid.Create("Shipping", instanceName);
var prometheusPortString = args.Skip(1).FirstOrDefault();

Console.Title = title;

var builder = Host.CreateApplicationBuilder(args);

var endpointConfiguration = PrepareEndpointConfiguration(instanceId, instanceName);

if (prometheusPortString != null)
{
    OpenTelemetryUtils.ConfigureOpenTelemetry("Shipping", instanceId.ToString(), int.Parse(prometheusPortString));
}

builder.UseNServiceBus(endpointConfiguration);
await builder.Build().RunAsync();

EndpointConfiguration PrepareEndpointConfiguration(Guid guid, string s)
{
    var cfg = new EndpointConfiguration("Shipping");
    cfg.LimitMessageProcessingConcurrencyTo(4);
    cfg.CustomDiagnosticsWriter((diagnostics, ct) => Task.CompletedTask);

    var serializer = cfg.UseSerialization<SystemJsonSerializer>();
    serializer.Options(new JsonSerializerOptions
    {
        TypeInfoResolverChain =
        {
            MessagesSerializationContext.Default
        }
    });

    //var transport = new LearningTransport
    //{
    //    StorageDirectory = Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location)!.Parent!.FullName, ".learningtransport")
    //};
    var azureConnectionString = Environment.GetEnvironmentVariable("AzureServiceBus_ConnectionString")!;
    var transport = new AzureServiceBusTransport(azureConnectionString, TopicTopology.Default)
        {
            TransportTransactionMode = TransportTransactionMode.ReceiveOnly
        };
    cfg.UseTransport(transport);

    cfg.AuditProcessedMessagesTo("audit");
    cfg.SendHeartbeatTo("Particular.ServiceControl");

    cfg.UniquelyIdentifyRunningInstance()
        .UsingCustomIdentifier(guid)
        .UsingCustomDisplayName(s);

    var metrics = cfg.EnableMetrics();
    metrics.SendMetricDataToServiceControl(
        "Particular.Monitoring",
        TimeSpan.FromMilliseconds(500)
    );

    var queueBindings = cfg.GetSettings().Get<QueueBindings>();
    queueBindings.BindSending("Particular.Monitoring");
    queueBindings.BindSending("Particular.ServiceControl");

    cfg.UsePersistence<NonDurablePersistence>();
    cfg.EnableOutbox();

    cfg.EnableOpenTelemetry();
    cfg.EnableInstallers();

    return cfg;
}