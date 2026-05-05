using NServiceBus;

namespace Messages;

public class RaffleWinnerSelected : IMessage
{
    public required string OrderId { get; set; }
    public required string Winner { get; set; }
}