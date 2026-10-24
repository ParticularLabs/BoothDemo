using Messages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using NServiceBus.Extensions.Logging;
using NServiceBus.Configuration.AdvancedExtensibility;
using NServiceBus.Transport;
using Shared;
using System.Text.Json;

namespace ClientUI;

/// <summary>
/// Runs the ClientUI endpoint and, in robust mode, the bridge next to it. Switching the mode recreates both, like
/// <see cref="ProcessingEndpointControls"/> does for the processing endpoints.
/// <list type="bullet">
/// <item>Normal mode: the endpoint uses RabbitMQ directly, so orders fail while RabbitMQ is unreachable.</item>
/// <item>Robust mode: the endpoint uses the PostgreSQL transport on a local database, and a bridge in this process
/// forwards the Sales queue to RabbitMQ. While RabbitMQ is unreachable, orders wait in the local Sales queue.</item>
/// </list>
/// </summary>
class ClientEndpointControls(
    UserInterface ui,
    SimulatedCustomers simulatedCustomers,
    string rabbitMqConnectionString,
    string postgreSqlConnectionString,
    Guid instanceId,
    string instanceName,
    ILoggerFactory loggerFactory)
{
    // Queues in RabbitMQ that the endpoint sends to. In robust mode the bridge creates a local queue for each of them.
    static readonly string[] rabbitMqDestinations = ["Sales", "Particular.ServiceControl", "Particular.Monitoring", "audit", "error"];

    static readonly TimeSpan retryDelay = TimeSpan.FromSeconds(5);
    static readonly TimeSpan stopTimeout = TimeSpan.FromSeconds(10);

    readonly SemaphoreSlim restartRequested = new(0);
    readonly CancellationTokenSource stopping = new();
    volatile bool robust;
    CancellationTokenSource? currentMode;
    Task? controlLoop;
    long? ordersWaitingLocally;

    public void BindRobustModeToggle(char key)
    {
        ui.BindToggle(key, "Robust mode", () => SwitchMode(true), () => SwitchMode(false));
        ui.BindInfo("Orders waiting in local PostgreSQL", () => robust ? ordersWaitingLocally?.ToString() ?? "?" : null);
    }

    void SwitchMode(bool robustMode)
    {
        robust = robustMode;
        // Abandon whatever the current mode is still trying to connect to
        currentMode?.Cancel();
        restartRequested.Release();
    }

    public void Start()
    {
        restartRequested.Release();
        controlLoop = Task.Run(RunControlLoop);
    }

#pragma warning disable PS0018
    public async Task Stop()
#pragma warning restore PS0018
    {
        await stopping.CancelAsync();
        if (controlLoop is not null)
        {
            await controlLoop;
        }
    }

#pragma warning disable PS0018
    async Task RunControlLoop()
#pragma warning restore PS0018
    {
        IEndpointInstance? endpoint = null;
        Task? background = null;

        while (true)
        {
            try
            {
                await restartRequested.WaitAsync(stopping.Token);
            }
            catch (OperationCanceledException) when (stopping.Token.IsCancellationRequested)
            {
                break;
            }

            // Several quick toggles result in a single restart
            while (restartRequested.Wait(0))
            {
            }

            await StopMode(endpoint, background);
            endpoint = null;
            background = null;

            currentMode = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
            var robustMode = robust;

            endpoint = await StartEndpoint(robustMode, currentMode.Token);
            if (endpoint is null)
            {
                continue;
            }

            simulatedCustomers.Connected(endpoint);

            if (robustMode)
            {
                background = Task.WhenAll(RunBridge(currentMode.Token), MonitorLocalQueue(currentMode.Token));
            }
        }

        await StopMode(endpoint, background);
    }

#pragma warning disable PS0018
    async Task StopMode(IEndpointInstance? endpoint, Task? background)
#pragma warning restore PS0018
    {
        simulatedCustomers.Disconnected();

        if (currentMode is not null)
        {
            await currentMode.CancelAsync();
        }

        if (background is not null)
        {
            await background;
        }

        if (endpoint is not null)
        {
            using var timeout = new CancellationTokenSource(stopTimeout);
            try
            {
                await endpoint.Stop(timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.Token.IsCancellationRequested)
            {
                // The broker went away while stopping; nothing more to clean up
            }
        }

        currentMode?.Dispose();
        currentMode = null;
    }

    // Retries until the endpoint starts, or returns null when the mode changes or the application stops in the meantime
    async Task<IEndpointInstance?> StartEndpoint(bool robustMode, CancellationToken cancellationToken)
    {
        var target = robustMode ? "PostgreSQL" : "RabbitMQ";

        while (true)
        {
            try
            {
                // A stopped bridge host leaves NServiceBus logging to its disposed logger factory, so point it back to ours
                NServiceBus.Logging.LogManager.UseFactory(new ExtensionsLoggerFactory(loggerFactory));
                var endpoint = await Endpoint.Start(CreateEndpointConfiguration(robustMode), cancellationToken);
                await ui.SendEvent(new ConnectionStatus(robustMode ? "ROBUST" : "NORMAL",
                    robustMode ? "Sending orders to the local PostgreSQL database, the bridge forwards them to RabbitMQ" : "Sending orders to RabbitMQ", true));
                return endpoint;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (Exception ex)
            {
                await ui.SendEvent(new BrokerUnavailable($"Cannot connect to {target} ({Describe(ex)}), retrying in {retryDelay.TotalSeconds:0} seconds"));
            }

            try
            {
                await Task.Delay(retryDelay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }
    }

    // Runs the bridge until the mode changes, starting it again whenever starting fails (RabbitMQ unreachable)
    async Task RunBridge(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var host = CreateBridgeHost();

            try
            {
                await host.StartAsync(cancellationToken);
                await ui.SendEvent(new ConnectionStatus("BRIDGE", "Connected to RabbitMQ, forwarding orders", true));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                await ui.SendEvent(new ConnectionStatus("BRIDGE",
                    $"Cannot connect to RabbitMQ ({Describe(ex)}), orders wait in the local queue. Retrying in {retryDelay.TotalSeconds:0} seconds", false));

                try
                {
                    await Task.Delay(retryDelay, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                continue;
            }

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                using var timeout = new CancellationTokenSource(stopTimeout);
                await host.StopAsync(timeout.Token);
            }
        }
    }

    // Counts the orders in the local Sales queue, which grows while RabbitMQ is unreachable
    async Task MonitorLocalQueue(CancellationToken cancellationToken)
    {
        ordersWaitingLocally = null;

        await using var dataSource = NpgsqlDataSource.Create(postgreSqlConnectionString);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var command = dataSource.CreateCommand("SELECT count(*) FROM public.\"Sales\"");
                ordersWaitingLocally = (long?)await command.ExecuteScalarAsync(cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                // The queue doesn't exist until the endpoint has created it
                ordersWaitingLocally = null;
            }
        }
    }

    // Short enough for one line on screen; the full exception goes to the logs
    static string Describe(Exception ex)
    {
        var message = ex.GetBaseException().Message.ReplaceLineEndings(" ");
        return message.Length <= 100 ? message : message[..100] + "...";
    }

    IHost CreateBridgeHost()
    {
        var bridgeConfiguration = new BridgeConfiguration();

        var postgreSql = new BridgeTransport(new PostgreSqlTransport(postgreSqlConnectionString))
        {
            Name = "postgresql",
            // The default, bridge.error, would be read as table error in schema bridge
            ErrorQueue = "bridge_error",
            AutoCreateQueues = true
        };
        postgreSql.HasEndpoint("ClientUI");

        var rabbitMq = new BridgeTransport(new StoreAndForwardTransport(CreateRabbitMqTransport(), ReportForwarding))
        {
            Name = "rabbitmq",
            AutoCreateQueues = true
        };
        foreach (var destination in rabbitMqDestinations)
        {
            rabbitMq.HasEndpoint(destination);
        }

        bridgeConfiguration.AddTransport(postgreSql);
        bridgeConfiguration.AddTransport(rabbitMq);
        bridgeConfiguration.RunInReceiveOnlyTransactionMode();

        // RabbitMQ keeps reconnecting, so keep the bridge running instead of stopping it
        bridgeConfiguration.DefineCriticalErrorAction((context, _) =>
            ui.SendEvent(new ConnectionStatus("BRIDGE", $"RabbitMQ unreachable: {context.Error}", false)).AsTask());

        // An empty builder: no console logging or configuration sources that would interfere with the UI
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Logging.AddOtlpLogging("ClientUI", instanceId.ToString());
        builder.UseNServiceBusBridge(bridgeConfiguration);
        return builder.Build();
    }

    void ReportForwarding(bool available) =>
        _ = ui.SendEvent(available
            ? new ConnectionStatus("BRIDGE", "RabbitMQ is back, forwarding the waiting orders", true)
            : new ConnectionStatus("BRIDGE", "Cannot forward to RabbitMQ, orders wait in the local queue", false));

    RabbitMQTransport CreateRabbitMqTransport() =>
        new(RoutingTopology.Conventional(QueueType.Quorum, true), rabbitMqConnectionString)
        {
            // Notice a pulled cable within seconds rather than a minute, and reconnect quickly once it is back
            HeartbeatInterval = TimeSpan.FromSeconds(5),
            NetworkRecoveryInterval = TimeSpan.FromSeconds(2)
        };

    EndpointConfiguration CreateEndpointConfiguration(bool robustMode)
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

        TransportDefinition transport = robustMode
            ? new PostgreSqlTransport(postgreSqlConnectionString)
            : CreateRabbitMqTransport();
        var routing = endpointConfiguration.UseTransport(transport);

        // The PostgreSQL transport reads a dot in a queue name as a schema separator, so quote names like Particular.Monitoring
        string Address(string queue) => robustMode && queue.Contains('.') ? $"\"{queue}\"" : queue;

        var queueBindings = endpointConfiguration.GetSettings().Get<QueueBindings>();
        queueBindings.BindSending(Address("Particular.Monitoring"));
        queueBindings.BindSending(Address("Particular.ServiceControl"));
        if (robustMode)
        {
            // Orders can be sent even if the bridge hasn't started yet to create the queue
            queueBindings.BindSending("Sales");
        }

        endpointConfiguration.AuditProcessedMessagesTo("audit");
        endpointConfiguration.SendHeartbeatTo(Address("Particular.ServiceControl"));

        endpointConfiguration.UniquelyIdentifyRunningInstance()
            .UsingCustomIdentifier(instanceId)
            .UsingCustomDisplayName(instanceName);
        endpointConfiguration.EnableInstallers();

        var metrics = endpointConfiguration.EnableMetrics();
        metrics.SendMetricDataToServiceControl(
            //"Particular.Monitoring.RabbitMQ",
            Address("Particular.Monitoring"),
            TimeSpan.FromMilliseconds(5000)
        );

        routing.RouteToEndpoint(typeof(PlaceOrder), "Sales");

        // The transport raises a critical error when the broker stays unreachable. Keep the endpoint running (the
        // transport keeps reconnecting) instead of shutting it down, and tell the person at the booth.
        endpointConfiguration.DefineCriticalErrorAction((context, _) =>
            ui.SendEvent(new BrokerUnavailable($"RabbitMQ unreachable: {context.Error}")).AsTask());

        return endpointConfiguration;
    }
}
