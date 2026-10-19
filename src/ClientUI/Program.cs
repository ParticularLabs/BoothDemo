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
var prometheusPortString = args.Skip(1).FirstOrDefault();

if (prometheusPortString != null)
{
    OpenTelemetryUtils.ConfigureOpenTelemetry("ClientUI", instanceId.ToString(), int.Parse(prometheusPortString));
}
var endpointConfiguration = PrepareEndpointConfiguration(instanceId, instanceName, prometheusPortString);

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.ClearProviders(); // removes Console, Debug, etc.
});
LogManager.UseFactory(new ExtensionsLoggerFactory(loggerFactory));

var endpointInstance = await Endpoint.Start(endpointConfiguration);
var ui = new UserInterface();
var simulatedCustomers = new SimulatedCustomers(endpointInstance, ui);
var cancellation = new CancellationTokenSource();

simulatedCustomers.BindSendingRateDial('q', 'a');
simulatedCustomers.BindDuplicateLikelihoodDial( 'w', 's');
simulatedCustomers.BindNoiseLevelDial('e', 'd');

simulatedCustomers.BindManualSendButton('b');
simulatedCustomers.BindRaffleMode('r');

var simulatedWork = simulatedCustomers.Run(cancellation.Token);

await ui.RunLoop(title);

cancellation.Cancel();

await simulatedWork;

await endpointInstance.Stop();


EndpointConfiguration PrepareEndpointConfiguration(Guid guid, string s, string? prometheusPortString1)
{
    var cfg = new EndpointConfiguration("ClientUI");
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

    var connectionString =  Environment.GetEnvironmentVariable("RabbitMQTransport_ConnectionString")!;
    var transport = new RabbitMQTransport(RoutingTopology.Conventional(QueueType.Quorum, true), connectionString);
    var routing = cfg.UseTransport(transport);
    routing.RouteToEndpoint(typeof(PlaceOrder), "Sales");

    cfg.UniquelyIdentifyRunningInstance()
        .UsingCustomIdentifier(guid)
        .UsingCustomDisplayName(s);

    var metrics = cfg.EnableMetrics();
    metrics.SendMetricDataToServiceControl(
        "Particular.Monitoring",
        TimeSpan.FromMilliseconds(5000)
    );

    var queueBindings = cfg.GetSettings().Get<QueueBindings>();
    queueBindings.BindSending("Particular.Monitoring");
    queueBindings.BindSending("Particular.ServiceControl");

    cfg.AuditProcessedMessagesTo("audit");
    cfg.SendHeartbeatTo("Particular.ServiceControl");
    cfg.EnableInstallers();

    return cfg;
}