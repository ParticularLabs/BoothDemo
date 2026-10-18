using Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

Console.Title = "Bridge";

var bridgeConfiguration = new BridgeConfiguration();
var rabbitMqConnectionString = Environment.GetEnvironmentVariable("RabbitMQTransport_ConnectionString")!;
var azureConnectionString = Environment.GetEnvironmentVariable("AzureServiceBus_ConnectionString")!;

var rabbit = new BridgeTransport(new RabbitMQTransport(RoutingTopology.Conventional(QueueType.Quorum), rabbitMqConnectionString))
{
    AutoCreateQueues = true
};
var asb = new BridgeTransport(new AzureServiceBusTransport(azureConnectionString, TopicTopology.Default))
{
    AutoCreateQueues = true
};

var salesEndpoint = new BridgeEndpoint("Sales-Bridge", "Sales");
salesEndpoint.RegisterPublisher<OrderBilled>("Billing");
salesEndpoint.RegisterPublisher<OrderShipped>("Shipping");
rabbit.HasEndpoint(salesEndpoint);

rabbit.HasEndpoint("ClientUI");

var billingEndpoint = new BridgeEndpoint("Billing");
billingEndpoint.RegisterPublisher<OrderPlaced>("Sales-Bridge");
asb.HasEndpoint(billingEndpoint);

var shippingEndpoint = new BridgeEndpoint("Shipping");
shippingEndpoint.RegisterPublisher<OrderPlaced>("Sales-Bridge");
asb.HasEndpoint(shippingEndpoint);

var raffleEndpoint = new BridgeEndpoint("Raffle");
raffleEndpoint.RegisterPublisher<RaffleOrderCompleted>("Sales-Bridge");
asb.HasEndpoint(raffleEndpoint);

//HINT: Bridge Platform queues to Azure
//asb.HasEndpoint("Particular.ServiceControl");
//asb.HasEndpoint("Particular.Monitoring");
//asb.HasEndpoint("error");
//asb.HasEndpoint("audit");

bridgeConfiguration.AddTransport(rabbit);
bridgeConfiguration.AddTransport(asb);

var builder = Host.CreateApplicationBuilder();
builder.UseNServiceBusBridge(bridgeConfiguration);

//HINT: Send Sales queue length metrics to RabbitMQ Particular.Monitoring queue

//// Reports the length of the RabbitMQ Sales queue to Monitoring. The message goes to the Particular.Monitoring
//// queue in RabbitMQ, from where the bridge forwards it to Azure Service Bus.
//// It is reported as Sales@rabbitmq because Monitoring itself tracks the bridge's Sales queue in Azure Service Bus.
//// Both show up summed as the queue length of the Sales endpoint.
//builder.Services.AddSingleton<IQueueLengthProvider>(new RabbitMQManagementQueueLengthProvider("http://localhost:15672"));
//builder.Services.AddHostedService(sp => new QueueLengthReporter(
//    new RabbitMQTransport(RoutingTopology.Conventional(QueueType.Quorum), rabbitMqConnectionString),
//    sp.GetRequiredService<IQueueLengthProvider>(),
//    [new MonitoredQueue("Sales", "Sales", ReportedQueueName: "Sales")],
//    sp.GetRequiredService<ILogger<QueueLengthReporter>>(),
//    // Monitoring does not interpolate between reports and its shortest graph (1 minute) has 1-second intervals
//    interval: TimeSpan.FromSeconds(1)));

await builder.Build().RunAsync();

