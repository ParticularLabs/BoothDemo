using NServiceBus;

namespace Messages;

public class OrderShipped : IEvent
{
    public required string OrderId { get; set; }
}