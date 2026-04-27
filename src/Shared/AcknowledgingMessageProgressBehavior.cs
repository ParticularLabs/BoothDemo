using NServiceBus.Pipeline;

namespace Shared;

using System;

public class AcknowledgingMessageProgressBehavior(UserInterface userInterface) : Behavior<ITransportReceiveContext>
{
    private FailureSimulator failureSimulator = new(userInterface);

    public override async Task Invoke(ITransportReceiveContext context, Func<Task> next)
    {
        await userInterface.SendEvent(new ProcessingStarted(context.Message.MessageId));

        try
        {
            await next().ConfigureAwait(false);

            if (context.Message.Headers.ContainsKey("MonitoringDemo.ManualMode"))
            {
                await failureSimulator.RunInteractive(context.Message.MessageId, ProcessingStage.Acknowledging, context.CancellationToken);
            }

            await userInterface.SendEvent(new ProcessingCompleted(context.Message.MessageId));
        }
        catch (Exception e)
        {
            await userInterface.SendEvent(new ProcessingFailed(context.Message.MessageId, e.Message));
            throw;
        }
    }

    public void Failure()
    {
        failureSimulator.Trigger();
    }
}