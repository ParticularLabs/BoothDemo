using NServiceBus.Pipeline;

namespace Shared;

public class ProcessingMessageProgressBehavior(UserInterface userInterface) : Behavior<IIncomingLogicalMessageContext>
{
    private FailureSimulator failureSimulator = new(userInterface);

    public override async Task Invoke(IIncomingLogicalMessageContext context, Func<Task> next)
    {
        if (context.Headers.ContainsKey("MonitoringDemo.ManualMode"))
        {
            await failureSimulator.RunInteractive(context.MessageId, ProcessingStage.Processing, context.CancellationToken);
        }

        await next().ConfigureAwait(false);
    }

    public void Failure()
    {
        failureSimulator.Trigger();
    }
}