using Messages;
using Microsoft.Extensions.Logging;
using NServiceBus;
using NServiceBus.Extensions.Logging;
using NServiceBus.Logging;
using Shared;
using System.Reflection;
using System.Text.Json;
using NServiceBus.Configuration.AdvancedExtensibility;
using NServiceBus.Transport;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var instancePostfix = args.FirstOrDefault();
var title = string.IsNullOrEmpty(instancePostfix) ? "Processing (Sales)" : $"Sales - {instancePostfix}";
var instanceName = string.IsNullOrEmpty(instancePostfix) ? "sales" : $"sales-{instancePostfix}";
var prometheusPortString = args.Skip(1).FirstOrDefault();

var instanceId = DeterministicGuid.Create("Sales", instanceName);

var ui = new UserInterface();
var endpointControls = new ProcessingEndpointControls(() => PrepareEndpointConfiguration(instanceId, instanceName, prometheusPortString), ui);

endpointControls.BindProcessingTimeDial('q', 'a');
endpointControls.BindSimulatedFailuresDial('w', 's');

endpointControls.BindDatabaseDownSimulationToggle('i');
endpointControls.BindDelayedRetriesToggle('o');
endpointControls.BindAutoThrottleToggle('p');

endpointControls.BindFailureProcessingButton(ui, 'x');

if (prometheusPortString != null)
{
    OpenTelemetryUtils.ConfigureOpenTelemetry("Sales", instanceId.ToString(), int.Parse(prometheusPortString));
}

endpointControls.Start();

await ui.RunLoop(title);

await endpointControls.StopEndpoint();

EndpointConfiguration PrepareEndpointConfiguration(Guid guid, string displayName, string? prometheusPortString1)
{
    var endpointConfiguration = new EndpointConfiguration("Sales");
    endpointConfiguration.LimitMessageProcessingConcurrencyTo(1);

    var serializer = endpointConfiguration.UseSerialization<SystemJsonSerializer>();
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
    var connectionString = Environment.GetEnvironmentVariable("RabbitMQTransport_ConnectionString")!;
    var transport = new RabbitMQTransport(RoutingTopology.Conventional(QueueType.Quorum, true), connectionString);
    endpointConfiguration.UseTransport(transport);
    var queueBindings = endpointConfiguration.GetSettings().Get<QueueBindings>();
    queueBindings.BindSending("Particular.Monitoring");
    queueBindings.BindSending("Particular.ServiceControl");

    endpointConfiguration.AuditProcessedMessagesTo("audit");
    endpointConfiguration.SendHeartbeatTo("Particular.ServiceControl");

    endpointConfiguration.UniquelyIdentifyRunningInstance()
        .UsingCustomIdentifier(guid)
        .UsingCustomDisplayName(displayName);

    var metrics = endpointConfiguration.EnableMetrics();

    metrics.SendMetricDataToServiceControl(
        "Particular.Monitoring",
        TimeSpan.FromMilliseconds(500)
    );

    endpointConfiguration.UsePersistence<NonDurablePersistence>();
    endpointConfiguration.EnableOutbox();

    endpointConfiguration.EnableOpenTelemetry();
    endpointConfiguration.EnableInstallers();

    var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.ClearProviders(); // removes Console, Debug, etc.

        // Optionally add something else:
        // builder.AddDebug();
        // builder.AddProvider(new MyCustomProvider());
    });

    LogManager.UseFactory(new ExtensionsLoggerFactory(loggerFactory));

    return endpointConfiguration;
}