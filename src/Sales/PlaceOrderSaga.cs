using Messages;
using Shared;

namespace Sales;

public class PlaceOrderSaga(UserInterface ui) : Saga<PlaceOrderSagaData>, IAmStartedByMessages<PlaceOrder>, IHandleMessages<OrderBilled>, IHandleMessages<OrderShipped>
{
    protected override void ConfigureHowToFindSaga(SagaPropertyMapper<PlaceOrderSagaData> mapper)
    {
        mapper.MapSaga(s => s.OrderId)
            .ToMessage<PlaceOrder>(m => m.OrderId)
            .ToMessage<OrderBilled>(m => m.OrderId)
            .ToMessage<OrderShipped>(m => m.OrderId);
    }

    public async Task Handle(PlaceOrder message, IMessageHandlerContext context)
    {
        var orderPlaced = new OrderPlaced
        {
            OrderId = message.OrderId
        };

        var publishOptions = new PublishOptions();
        publishOptions.SetMessageId(MessageIdHelper.GetHumanReadableMessageId());
        await context.Publish(orderPlaced, publishOptions);
        await ui.SendEvent(new OrderSagaStarted(message.OrderId));
    }

    public Task Handle(OrderBilled message, IMessageHandlerContext context)
    {
        Data.IsBilled = true;
        return TryComplete();
    }

    public Task Handle(OrderShipped message, IMessageHandlerContext context)
    {
        Data.IsShipped = true;
        return TryComplete();
    }

#pragma warning disable PS0018
    async Task TryComplete()
#pragma warning restore PS0018
    {
        if (Data.IsBilled && Data.IsShipped)
        {
            MarkAsComplete();
            await ui.SendEvent(new OrderSagaCompleted(Data.OrderId));
        }
    }
}

public class PlaceOrderSagaData : ContainSagaData
{
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    public string OrderId { get; set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    public bool IsShipped { get; set; }
    public bool IsBilled { get; set; }
}
