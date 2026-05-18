using Messages;
using Microsoft.Extensions.Logging;

namespace Raffle;

public class RaffleWinnerSelectedHandler(ILogger<RaffleOrderCompletedHandler> logger) : IHandleMessages<RaffleWinnerSelected>
{
    public Task Handle(RaffleWinnerSelected message, IMessageHandlerContext context)
    {
        var orderId = message.OrderId;
        logger.LogInformation("Raffle winner selected for order {orderId}", orderId);

        throw new Exception("And the winner is....");

        //HINT: We need to handle it to audit the message
        //return Task.CompletedTask;
    }
}