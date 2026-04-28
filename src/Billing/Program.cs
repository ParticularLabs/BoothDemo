using Messages;
using Microsoft.Extensions.Hosting;
using NServiceBus.Configuration.AdvancedExtensibility;
using NServiceBus.Transport;
using Shared;
using System.Text.Json;

var instancePostfix = args.FirstOrDefault();

var title = string.IsNullOrEmpty(instancePostfix) ? "Failure rate (Billing)" : $"Billing - {instancePostfix}";
var instanceName = string.IsNullOrEmpty(instancePostfix) ? "billing" : $"billing-{instancePostfix}";
var instanceId = DeterministicGuid.Create("Billing", instanceName);
var prometheusPortString = args.Skip(1).FirstOrDefault();

Console.Title = title;

var builder = Host.CreateApplicationBuilder(args);

var endpointConfiguration = PrepareEndpointConfiguration(instanceId, instanceName, prometheusPortString);

if (prometheusPortString != null)
{
    OpenTelemetryUtils.ConfigureOpenTelemetry("Billing", instanceId.ToString(), int.Parse(prometheusPortString));
}

builder.UseNServiceBus(endpointConfiguration);
await builder.Build().RunAsync();

EndpointConfiguration PrepareEndpointConfiguration(Guid guid, string s, string? prometheusPortString1)
{
    var cfg = new EndpointConfiguration("Billing");
    cfg.LimitMessageProcessingConcurrencyTo(1);

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
    //    StorageDirectory = Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location)!.Parent!.FullName, ".learningtransport"),
    //    TransportTransactionMode = TransportTransactionMode.ReceiveOnly
    //};
    var azureConnectionString = Environment.GetEnvironmentVariable("AzureServiceBus_ConnectionString")!;
    var transport = new AzureServiceBusTransport(azureConnectionString, TopicTopology.Default)
        {
            TransportTransactionMode = TransportTransactionMode.ReceiveOnly
        };
    cfg.UseTransport(transport);

    //cfg.Recoverability()
    //    .Delayed(delayed => delayed.NumberOfRetries(0));

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