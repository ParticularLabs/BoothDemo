using Messages;
using Microsoft.Extensions.Logging;

namespace Raffle;

public class RaffleOrderCompletedHandler(ILogger<RaffleOrderCompletedHandler> logger) : IHandleMessages<RaffleOrderCompleted>
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    public static string[] Names;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

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