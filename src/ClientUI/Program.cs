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

var endpointConfiguration = new EndpointConfiguration("ClientUI");

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
//    StorageDirectory = Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location)!.Parent!.FullName, ".learningtransport")
//};
var connectionString = Environment.GetEnvironmentVariable("RabbitMQTransport_ConnectionString")!;
var transport = new RabbitMQTransport(RoutingTopology.Conventional(QueueType.Quorum, true), connectionString);
var routing = endpointConfiguration.UseTransport(transport);

var queueBindings = endpointConfiguration.GetSettings().Get<QueueBindings>();
queueBindings.BindSending("Particular.Monitoring");
queueBindings.BindSending("Particular.ServiceControl");

endpointConfiguration.AuditProcessedMessagesTo("audit");
endpointConfiguration.SendHeartbeatTo("Particular.ServiceControl");

endpointConfiguration.UniquelyIdentifyRunningInstance()
    .UsingCustomIdentifier(instanceId)
    .UsingCustomDisplayName(instanceName);

endpointConfiguration.EnableOpenTelemetry();
endpointConfiguration.EnableInstallers();

var metrics = endpointConfiguration.EnableMetrics();
metrics.SendMetricDataToServiceControl(
    "Particular.Monitoring",
    TimeSpan.FromMilliseconds(5000)
);

routing.RouteToEndpoint(typeof(PlaceOrder), "Sales");

if (prometheusPortString != null)
{
    OpenTelemetryUtils.ConfigureOpenTelemetry("ClientUI", instanceId.ToString(), int.Parse(prometheusPortString));
}

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.ClearProviders(); // removes Console, Debug, etc.

    // Optionally add something else:
    // builder.AddDebug();
    // builder.AddProvider(new MyCustomProvider());
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