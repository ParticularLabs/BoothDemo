using Messages;
using Microsoft.Extensions.Logging;
using NServiceBus.Logging;
using Shared;
using System.Reflection;
using System.Text.Json;
using NServiceBus.Extensions.Logging;

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
    var endpointConfiguration1 = new EndpointConfiguration("Sales");
    endpointConfiguration1.LimitMessageProcessingConcurrencyTo(1);

    var serializer = endpointConfiguration1.UseSerialization<SystemJsonSerializer>();
    serializer.Options(new JsonSerializerOptions
    {
        TypeInfoResolverChain =
        {
            MessagesSerializationContext.Default
        }
    });

    var transport = new LearningTransport
    {
        StorageDirectory = Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location)!.Parent!.FullName, ".learningtransport"),
        TransportTransactionMode = TransportTransactionMode.ReceiveOnly
    };
    endpointConfiguration1.UseTransport(transport);

    endpointConfiguration1.AuditProcessedMessagesTo("audit");
    endpointConfiguration1.SendHeartbeatTo("Particular.ServiceControl");

    endpointConfiguration1.UniquelyIdentifyRunningInstance()
        .UsingCustomIdentifier(guid)
        .UsingCustomDisplayName(displayName);

    var metrics = endpointConfiguration1.EnableMetrics();

    metrics.SendMetricDataToServiceControl(
        "Particular.Monitoring",
        TimeSpan.FromMilliseconds(500)
    );

    endpointConfiguration1.UsePersistence<NonDurablePersistence>();
    endpointConfiguration1.EnableOutbox();

    endpointConfiguration1.EnableOpenTelemetry();

    var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.ClearProviders(); // removes Console, Debug, etc.

        // Optionally add something else:
        // builder.AddDebug();
        // builder.AddProvider(new MyCustomProvider());
    });

    LogManager.UseFactory(new ExtensionsLoggerFactory(loggerFactory));

    return endpointConfiguration1;
}