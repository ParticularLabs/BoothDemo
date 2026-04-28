using Messages;
using Microsoft.Extensions.Logging;
using Shared;

namespace Shipping;

public class OrderPlacedHandler(ILogger<OrderPlacedHandler> logger) : IHandleMessages<OrderPlaced>
{
    public async Task Handle(OrderPlaced message, IMessageHandlerContext context)
    {
        var orderShipped = new OrderShipped
        {
            OrderId = message.OrderId
        };

        var publishOptions = new PublishOptions();
        publishOptions.SetMessageId(MessageIdHelper.GetHumanReadableMessageId());
        await context.Publish(orderShipped, publishOptions);

        logger.LogInformation("Order {orderId} shipped", message.OrderId);
    }
}