using NServiceBus.Transport;

namespace ClientUI;

/// <summary>
/// Wraps the transport that the bridge forwards messages to. When a message can't be dispatched because the broker is
/// unreachable, the dispatch is retried until it succeeds instead of failing: the bridge would otherwise move the
/// message to its error queue after three attempts. Meanwhile the message stays in the local queue it was received
/// from, and the ones behind it wait their turn.
/// </summary>
sealed class StoreAndForwardTransport(TransportDefinition inner, Action<bool> availabilityChanged)
    : TransportDefinition(inner.TransportTransactionMode, inner.SupportsDelayedDelivery, inner.SupportsPublishSubscribe, inner.SupportsTTBR)
{
    static readonly TimeSpan retryDelay = TimeSpan.FromSeconds(2);

    public override async Task<TransportInfrastructure> Initialize(HostSettings hostSettings, ReceiveSettings[] receivers, string[] sendingAddresses, CancellationToken cancellationToken = default)
    {
        // The bridge sets the transaction mode on this definition
        inner.TransportTransactionMode = TransportTransactionMode;

        var infrastructure = await inner.Initialize(hostSettings, receivers, sendingAddresses, cancellationToken);
        return new Infrastructure(infrastructure, new RetryingDispatcher(infrastructure.Dispatcher, availabilityChanged));
    }

    public override IReadOnlyCollection<TransportTransactionMode> GetSupportedTransactionModes() => inner.GetSupportedTransactionModes();

    sealed class Infrastructure : TransportInfrastructure
    {
        readonly TransportInfrastructure inner;

        public Infrastructure(TransportInfrastructure inner, IMessageDispatcher dispatcher)
        {
            this.inner = inner;
            Dispatcher = dispatcher;
            Receivers = inner.Receivers;
        }

        public override Task Shutdown(CancellationToken cancellationToken = default) => inner.Shutdown(cancellationToken);

        public override string ToTransportAddress(QueueAddress address) => inner.ToTransportAddress(address);
    }

    sealed class RetryingDispatcher(IMessageDispatcher inner, Action<bool> availabilityChanged) : IMessageDispatcher
    {
        // Failures shorter than this aren't reported: they happen while the bridge stops, for example
        static readonly TimeSpan reportAfter = TimeSpan.FromSeconds(5);

        readonly Lock stateLock = new();
        DateTime? failingSince;
        bool reported;

        public async Task Dispatch(TransportOperations outgoingMessages, TransportTransaction transaction, CancellationToken cancellationToken = default)
        {
            while (true)
            {
                try
                {
                    await inner.Dispatch(outgoingMessages, transaction, cancellationToken);
                    Succeeded();
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
                {
                    Failed();
                }

                await Task.Delay(retryDelay, cancellationToken);
            }
        }

        void Succeeded()
        {
            bool recovered;
            lock (stateLock)
            {
                recovered = reported;
                failingSince = null;
                reported = false;
            }

            if (recovered)
            {
                availabilityChanged(true);
            }
        }

        void Failed()
        {
            bool report;
            lock (stateLock)
            {
                failingSince ??= DateTime.UtcNow;
                report = !reported && DateTime.UtcNow - failingSince >= reportAfter;
                reported |= report;
            }

            if (report)
            {
                availabilityChanged(false);
            }
        }
    }
}
