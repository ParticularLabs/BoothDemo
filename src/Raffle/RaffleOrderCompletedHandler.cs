using Messages;
using Microsoft.Extensions.Logging;

namespace Raffle;

public class RaffleOrderCompletedHandler(ILogger<RaffleOrderCompletedHandler> logger) : IHandleMessages<RaffleOrderCompleted>
{
    static readonly string[] names =
    [
        "Szymon Pobiega",
        "Andreas Öhlund"
    ];


    public Task Handle(RaffleOrderCompleted message, IMessageHandlerContext context)
    {
        var orderId = message.OrderId;
        logger.LogInformation("Selecting the raffle winner for order {orderId}", orderId);

        var random = Random.Shared;
        var winnerIndex = random.Next(names.Length);

        return context.SendLocal(new RaffleWinnerSelected
        {
            OrderId = message.OrderId,
            Winner = names[winnerIndex]
        });
    }
}