using Messages;
using Microsoft.Extensions.Logging;
using Shared;

namespace Billing;

public class OrderPlacedHandler(ILogger<OrderPlacedHandler> logger) : IHandleMessages<OrderPlaced>
{
    public async Task Handle(OrderPlaced message, IMessageHandlerContext context)
    {
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