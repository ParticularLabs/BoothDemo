using NServiceBus.Pipeline;
using NServiceBus.Transport;

namespace Shared;

public class DispatchingProgressBehavior(UserInterface userInterface) : Behavior<IBatchDispatchContext>
{
    FailureSimulator failureSimulator = new(userInterface);

    public override async Task Invoke(IBatchDispatchContext context, Func<Task> next)
    {
        await next().ConfigureAwait(false);

        var incomingMessage = context.Extensions.Get<IncomingMessage>();
        if (incomingMessage.Headers.ContainsKey("MonitoringDemo.ManualMode"))
        {
            await failureSimulator.RunInteractive(incomingMessage.MessageId, ProcessingStage.Dispatching, context.CancellationToken);
        }
    }

    public void Failure()
    {
        failureSimulator.Trigger();
    }
}