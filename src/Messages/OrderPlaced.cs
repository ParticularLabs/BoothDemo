using NServiceBus;

namespace Messages;

public class OrderPlaced : IEvent
{
    public required string OrderId { get; set; }
}