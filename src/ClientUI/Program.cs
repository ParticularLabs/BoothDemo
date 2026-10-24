using ClientUI;
using Messages;
using Microsoft.Extensions.Logging;
using NServiceBus.Configuration.AdvancedExtensibility;
using NServiceBus.Logging;
using NServiceBus.Transport;
using Shared;
using System.Text.Json;
using NServiceBus.Extensions.Logging;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var instancePostfix = args.FirstOrDefault();

var title = string.IsNullOrEmpty(instancePostfix) ? "ClientUI" : $"ClientUI - {instancePostfix}";
var instanceName = string.IsNullOrEmpty(instancePostfix) ? "clientui" : $"clientui-{instancePostfix}";
var instanceId = DeterministicGuid.Create("ClientUI", instanceName);

var connectionString = Environment.GetEnvironmentVariable("RabbitMQTransport_ConnectionString")!;

using var telemetry = OpenTelemetryUtils.ConfigureOpenTelemetry("ClientUI", instanceId.ToString());

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.ClearProviders(); // removes Console, Debug, etc.
    builder.AddOtlpLogging("ClientUI", instanceId.ToString());

    // Optionally add something else:
    // builder.AddDebug();
    // builder.AddProvider(new MyCustomProvider());
});

LogManager.UseFactory(new ExtensionsLoggerFactory(loggerFactory));

var ui = new UserInterface();
var simulatedCustomers = new SimulatedCustomers(ui);
var cancellation = new CancellationTokenSource();

simulatedCustomers.BindSendingRateDial('q', 'a');
simulatedCustomers.BindDuplicateLikelihoodDial( 'w', 's');
simulatedCustomers.BindNoiseLevelDial('e', 'd');

simulatedCustomers.BindManualSendButton('b');
simulatedCustomers.BindRaffleMode('r');

// The UI runs even while RabbitMQ is unreachable: the endpoint keeps trying to start in the background, and orders that
// can't be sent in the meantime are reported as failed requests.
var endpointStarting = StartEndpoint(cancellation.Token);
var simulatedWork = simulatedCustomers.Run(cancellation.Token);

await ui.RunLoop(title);

cancellation.Cancel();

await simulatedWork;

if (await endpointStarting is { } endpointInstance)
{
    await endpointInstance.Stop();
}

async Task<IEndpointInstance?> StartEndpoint(CancellationToken cancellationToken = default)
{
    while (true)
    {
        try
        {
            var instance = await Endpoint.Start(CreateEndpointConfiguration(), cancellationToken);
            simulatedCustomers.Connected(instance);
            return instance;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            await ui.SendEvent(new BrokerUnavailable($"Cannot connect to RabbitMQ ({ex.GetType().Name}), retrying in 5 seconds"));
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}

EndpointConfiguration CreateEndpointConfiguration()
{
    var endpointConfiguration = new EndpointConfiguration("ClientUI");

    var serializer = endpointConfiguration.UseSerialization<SystemJsonSerializer>();
    serializer.Options(new JsonSerializerOptions
    {
        TypeInfoResolverChain =
            {
                MessagesSerializationContext.Default
            }
    });

    var transport = new RabbitMQTransport(RoutingTopology.Conventional(QueueType.Quorum, true), connectionString)
    {
        // Notice a pulled cable within seconds rather than a minute, and reconnect quickly once it is back
        HeartbeatInterval = TimeSpan.FromSeconds(5),
        NetworkRecoveryInterval = TimeSpan.FromSeconds(2)
    };
    var routing = endpointConfiguration.UseTransport(transport);

    var queueBindings = endpointConfiguration.GetSettings().Get<QueueBindings>();
    queueBindings.BindSending("Particular.Monitoring");
    queueBindings.BindSending("Particular.ServiceControl");

    endpointConfiguration.AuditProcessedMessagesTo("audit");
    endpointConfiguration.SendHeartbeatTo("Particular.ServiceControl");

    endpointConfiguration.UniquelyIdentifyRunningInstance()
        .UsingCustomIdentifier(instanceId)
        .UsingCustomDisplayName(instanceName);
    endpointConfiguration.EnableInstallers();

    var metrics = endpointConfiguration.EnableMetrics();
    metrics.SendMetricDataToServiceControl(
        //"Particular.Monitoring.RabbitMQ",
        "Particular.Monitoring",
        TimeSpan.FromMilliseconds(5000)
    );

    routing.RouteToEndpoint(typeof(PlaceOrder), "Sales");

    // The transport raises a critical error when the broker stays unreachable. Keep the endpoint running (the transport
    // keeps reconnecting) instead of shutting it down, and tell the person at the booth.
    endpointConfiguration.DefineCriticalErrorAction((context, _) =>
        ui.SendEvent(new BrokerUnavailable($"RabbitMQ unreachable: {context.Error}")).AsTask());

    return endpointConfiguration;
}
