using System.Reflection;
using System.Text.Json;
using ClientUI;
using Messages;
using Shared;

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

var transport = new LearningTransport
{
    StorageDirectory = Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location)!.Parent!.FullName, ".learningtransport")
};
var routing = endpointConfiguration.UseTransport(transport);

endpointConfiguration.AuditProcessedMessagesTo("audit");
endpointConfiguration.SendHeartbeatTo("Particular.ServiceControl");

endpointConfiguration.UniquelyIdentifyRunningInstance()
    .UsingCustomIdentifier(instanceId)
    .UsingCustomDisplayName(instanceName);

endpointConfiguration.EnableOpenTelemetry();

var metrics = endpointConfiguration.EnableMetrics();
metrics.SendMetricDataToServiceControl(
    "Particular.Monitoring",
    TimeSpan.FromMilliseconds(500)
);

routing.RouteToEndpoint(typeof(PlaceOrder), "Sales");

if (prometheusPortString != null)
{
    OpenTelemetryUtils.ConfigureOpenTelemetry("ClientUI", instanceId.ToString(), int.Parse(prometheusPortString));
}

var endpointInstance = await Endpoint.Start(endpointConfiguration);
var ui = new UserInterface();
var simulatedCustomers = new SimulatedCustomers(endpointInstance, ui);
var cancellation = new CancellationTokenSource();

simulatedCustomers.BindSendingRateDial('q', 'a');
simulatedCustomers.BindDuplicateLikelihoodDial( 'w', 's');
simulatedCustomers.BindNoiseLevelDial('e', 'd');

simulatedCustomers.BindManualSendButton('b');

var simulatedWork = simulatedCustomers.Run(cancellation.Token);

await ui.RunLoop(title);

cancellation.Cancel();

await simulatedWork;

await endpointInstance.Stop();