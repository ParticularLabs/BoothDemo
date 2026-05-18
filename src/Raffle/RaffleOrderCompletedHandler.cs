using Messages;
using Microsoft.Extensions.Logging;

namespace Raffle;

public class RaffleOrderCompletedHandler(ILogger<RaffleOrderCompletedHandler> logger) : IHandleMessages<RaffleOrderCompleted>
{
    public static string[] Names;

    public Task Handle(RaffleOrderCompleted message, IMessageHandlerContext context)
    {
        var orderId = message.OrderId;
        logger.LogInformation("Selecting the raffle winner for order {orderId}", orderId);

        var random = Random.Shared;
        var winnerIndex = random.Next(Names.Length);

        return context.SendLocal(new RaffleWinnerSelected
        {
            OrderId = message.OrderId,
            Winner = Names[winnerIndex]
        });
    }
}