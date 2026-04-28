using NServiceBus;

namespace Messages;

public class PlaceOrder : ICommand
{
    public required string OrderId { get; set; }
}