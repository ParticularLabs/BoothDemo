using NServiceBus;

namespace Messages;

public class RaffleOrderCompleted : IEvent
{
    public required string OrderId { get; set; }
}