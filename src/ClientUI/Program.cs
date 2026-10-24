using ClientUI;
using Microsoft.Extensions.Logging;
using NServiceBus.Logging;
using Shared;
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

// Local database used by the PostgreSQL transport in robust mode
var postgreSqlConnectionString = Environment.GetEnvironmentVariable("PostgreSqlTransport_ConnectionString")
    ?? "Host=localhost;Port=5432;Username=nsb;Password=particular;Database=clientui";

var ui = new UserInterface();
var simulatedCustomers = new SimulatedCustomers(ui);
var endpointControls = new ClientEndpointControls(ui, simulatedCustomers, connectionString, postgreSqlConnectionString, instanceId, instanceName, loggerFactory);
var cancellation = new CancellationTokenSource();

simulatedCustomers.BindSendingRateDial('q', 'a');
simulatedCustomers.BindDuplicateLikelihoodDial( 'w', 's');
simulatedCustomers.BindNoiseLevelDial('e', 'd');

simulatedCustomers.BindManualSendButton('b');
simulatedCustomers.BindRaffleMode('f');
endpointControls.BindRobustModeToggle('r');

// The UI runs even while the broker is unreachable: the endpoint keeps trying to start in the background, and orders
// that can't be sent in the meantime are reported as failed requests.
endpointControls.Start();
var simulatedWork = simulatedCustomers.Run(cancellation.Token);

await ui.RunLoop(title);

cancellation.Cancel();

await simulatedWork;

await endpointControls.Stop();
