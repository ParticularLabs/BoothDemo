using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NServiceBus.Performance.TimeToBeReceived;
using NServiceBus.Routing;
using NServiceBus.Transport;

/// <summary>
/// A queue whose length is reported to ServiceControl Monitoring as the queue length of the given endpoint.
/// </summary>
/// <param name="EndpointName">The endpoint the queue length is attributed to in Monitoring.</param>
/// <param name="QueueName">The queue to read the length of.</param>
/// <param name="ReportedQueueName">
/// The queue name reported to Monitoring, if different from <paramref name="QueueName"/>. Monitoring sums the lengths of
/// all queues reported for an endpoint, so use a distinct name when Monitoring also tracks a queue with the same name
/// itself (e.g. a bridge proxy queue on its own transport); otherwise the two readings get averaged together.
/// </param>
record MonitoredQueue(string EndpointName, string QueueName, string? ReportedQueueName = null);

/// <summary>
/// Periodically reads queue lengths using an <see cref="IQueueLengthProvider"/> and sends them to ServiceControl Monitoring
/// using the raw transport, in the same format the NServiceBus.Metrics.ServiceControl package uses.
/// No NServiceBus endpoint is needed; the transport is only used for dispatching.
/// </summary>
sealed class QueueLengthReporter(
    TransportDefinition transport,
    IQueueLengthProvider queueLengthProvider,
    IReadOnlyCollection<MonitoredQueue> queues,
    ILogger<QueueLengthReporter> logger,
    string monitoringQueue = "Particular.Monitoring",
    TimeSpan? interval = null) : BackgroundService
{
    // Must match what ServiceControl Monitoring expects (see NServiceBus.Metrics.ServiceControl)
    const string MetricTypeHeader = "NServiceBus.Metric.Type";
    const string MetricInstanceIdHeader = "NServiceBus.Metric.InstanceId";
    const string QueueLengthMetricType = "QueueLength";
    const string TaggedLongValueContentType = "TaggedLongValueWriterOccurrence";
    const long WireFormatVersion = 1;

    // Queue lengths are only useful while fresh, so don't let them pile up while Monitoring is unavailable
    static readonly TimeSpan TimeToBeReceived = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var hostSettings = new HostSettings(
            nameof(QueueLengthReporter),
            nameof(QueueLengthReporter),
            new StartupDiagnosticEntries(),
            (message, exception, _) => logger.LogCritical(exception, message),
            setupInfrastructure: false);

        var infrastructure = await transport.Initialize(hostSettings, [], [monitoringQueue], cancellationToken);

        try
        {
            using var timer = new PeriodicTimer(interval ?? TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                try
                {
                    await Report(infrastructure.Dispatcher, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Error reporting queue lengths");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down
        }
        finally
        {
            await infrastructure.Shutdown(CancellationToken.None);
        }
    }

    async Task Report(IMessageDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var timestamp = DateTime.UtcNow.Ticks;
        var operations = new List<TransportOperation>();

        // Monitoring attributes queue length to the endpoint in the OriginatingEndpoint header, so one message per endpoint
        foreach (var endpointQueues in queues.GroupBy(q => q.EndpointName))
        {
            var lengths = new List<(string QueueName, long Length)>();
            foreach (var queue in endpointQueues)
            {
                lengths.Add((queue.ReportedQueueName ?? queue.QueueName, await queueLengthProvider.GetQueueLength(queue.QueueName, cancellationToken)));
            }

            var headers = new Dictionary<string, string>
            {
                [Headers.OriginatingEndpoint] = endpointQueues.Key,
                [Headers.ContentType] = TaggedLongValueContentType,
                [MetricTypeHeader] = QueueLengthMetricType,
                // All Monitoring handlers of TaggedLongValueOccurrence (not only the queue length one) resolve the endpoint
                // instance from the headers. Without this header they require OriginatingHostId and HostDisplayName instead.
                [MetricInstanceIdHeader] = nameof(QueueLengthReporter)
            };

            var message = new OutgoingMessage(Guid.NewGuid().ToString(), headers, Serialize(timestamp, lengths));
            var dispatchProperties = new DispatchProperties
            {
                DiscardIfNotReceivedBefore = new DiscardIfNotReceivedBefore(TimeToBeReceived)
            };
            operations.Add(new TransportOperation(message, new UnicastAddressTag(monitoringQueue), dispatchProperties));
        }

        await dispatcher.Dispatch(new TransportOperations([.. operations]), new TransportTransaction(), cancellationToken);
    }

    // Wire format V1 of TaggedLongValueWriter (all values little-endian):
    // version (long) | base ticks (long) | tag count (int) | [tag id (int) | tag length (int) | tag UTF-8 bytes]...
    // | entry count (int) | [ticks offset from base (int) | tag id (int) | value (long)]...
    static byte[] Serialize(long timestamp, List<(string QueueName, long Length)> lengths)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(WireFormatVersion);
        writer.Write(timestamp);

        writer.Write(lengths.Count);
        for (var i = 0; i < lengths.Count; i++)
        {
            var tagBytes = Encoding.UTF8.GetBytes(lengths[i].QueueName);
            writer.Write(TagId(i));
            writer.Write(tagBytes.Length);
            writer.Write(tagBytes);
        }

        writer.Write(lengths.Count);
        for (var i = 0; i < lengths.Count; i++)
        {
            writer.Write(0); // all entries share the base timestamp
            writer.Write(TagId(i));
            writer.Write(lengths[i].Length);
        }

        writer.Flush();
        return stream.ToArray();
    }

    // Tag IDs only need to be unique within a message; 0 is reserved for "no tag"
    static int TagId(int index) => index + 1;
}
