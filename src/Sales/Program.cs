using Messages;
using Microsoft.Extensions.Logging;
using Npgsql;
using NServiceBus;
using NServiceBus.Configuration.AdvancedExtensibility;
using NServiceBus.Extensions.Logging;
using NServiceBus.Logging;
using NServiceBus.Transport;
using Shared;
using System.Reflection;
using System.Text.Json;
using NpgsqlTypes;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var instancePostfix = args.FirstOrDefault();
var title = string.IsNullOrEmpty(instancePostfix) ? "Processing (Sales)" : $"Sales - {instancePostfix}";
var instanceName = string.IsNullOrEmpty(instancePostfix) ? "sales" : $"sales-{instancePostfix}";
var showHelp = instancePostfix is null or "1";

var instanceId = DeterministicGuid.Create("Sales", instanceName);

var ui = new UserInterface(showHelp);
var endpointControls = new ProcessingEndpointControls(() => PrepareEndpointConfiguration(instanceId, instanceName), ui);

endpointControls.BindProcessingTimeDial('q', 'a');
endpointControls.BindSimulatedFailuresDial('w', 's');

endpointControls.BindDatabaseDownSimulationToggle('i');
endpointControls.BindDelayedRetriesToggle('o');
endpointControls.BindAutoThrottleToggle('p');

endpointControls.BindFailureProcessingButton(ui, 'x');

endpointControls.Start();

await ui.RunLoop(title);

await endpointControls.StopEndpoint();

EndpointConfiguration PrepareEndpointConfiguration(Guid guid, string displayName)
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

    var connectionString = Environment.GetEnvironmentVariable("RabbitMQTransport_ConnectionString")!;
    var transport = new RabbitMQTransport(RoutingTopology.Conventional(QueueType.Quorum, true), connectionString);
    endpointConfiguration.UseTransport(transport);
    var queueBindings = endpointConfiguration.GetSettings().Get<QueueBindings>();
    queueBindings.BindSending("Particular.Monitoring");
    queueBindings.BindSending("Particular.ServiceControl");

    endpointConfiguration.AuditProcessedMessagesTo("audit");
    endpointConfiguration.AuditSagaStateChanges("audit");

    endpointConfiguration.SendHeartbeatTo("Particular.ServiceControl");

    endpointConfiguration.UniquelyIdentifyRunningInstance()
        .UsingCustomIdentifier(guid)
        .UsingCustomDisplayName(displayName);

    var metrics = endpointConfiguration.EnableMetrics();

    metrics.SendMetricDataToServiceControl(
        //"Particular.Monitoring.RabbitMQ",
        "Particular.Monitoring",
        TimeSpan.FromMilliseconds(5000)
    );

    //endpointConfiguration.UsePersistence<NonDurablePersistence>();

    endpointConfiguration.EnableOutbox();

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