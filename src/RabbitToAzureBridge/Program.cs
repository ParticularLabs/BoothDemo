using Messages;
using Microsoft.Extensions.Hosting;

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

var salesEndpoint = new BridgeEndpoint("Sales");
salesEndpoint.RegisterPublisher<OrderBilled>("Billing");
salesEndpoint.RegisterPublisher<OrderShipped>("Shipping");
rabbit.HasEndpoint(salesEndpoint);

var billingEndpoint = new BridgeEndpoint("Billing");
billingEndpoint.RegisterPublisher<OrderPlaced>("Sales");
asb.HasEndpoint(billingEndpoint);

var shippingEndpoint = new BridgeEndpoint("Shipping");
shippingEndpoint.RegisterPublisher<OrderPlaced>("Sales");
asb.HasEndpoint(shippingEndpoint);

asb.HasEndpoint("Particular.ServiceControl");
asb.HasEndpoint("Particular.Monitoring");
asb.HasEndpoint("error");
asb.HasEndpoint("audit");

bridgeConfiguration.AddTransport(rabbit);
bridgeConfiguration.AddTransport(asb);

var builder = Host.CreateApplicationBuilder();
builder.UseNServiceBusBridge(bridgeConfiguration);

await builder.Build().RunAsync();

