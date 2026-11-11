using Messages;
using Microsoft.Extensions.Logging;
using Shared;

namespace Billing;

public class OrderPlacedHandler(ILogger<OrderPlacedHandler> logger) : IHandleMessages<OrderPlaced>
{
    public async Task Handle(OrderPlaced message, IMessageHandlerContext context)
    {
        // Simulated billing work, so that a busy booth makes the endpoint scale out
        await Task.Delay(TimeSpan.FromMilliseconds(200), context.CancellationToken);

        var orderBilled = new OrderBilled
        {
            OrderId = message.OrderId
        };

        var publishOptions = new PublishOptions();
        publishOptions.SetMessageId(MessageIdHelper.GetHumanReadableMessageId());
        await context.Publish(orderBilled, publishOptions);

        logger.LogInformation("Order {orderId} billed", message.OrderId);
    }
}