/// <summary>
/// Plugin that knows how to read the number of messages in a queue of a given transport.
/// </summary>
interface IQueueLengthProvider
{
    Task<long> GetQueueLength(string queueName, CancellationToken cancellationToken = default);
}
