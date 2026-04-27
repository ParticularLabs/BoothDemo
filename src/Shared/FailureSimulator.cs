
namespace Shared;

public class FailureSimulator(UserInterface userInterface)
{
    private volatile bool failureTriggered = false;
    private volatile bool running = false;

#pragma warning disable PS0003
    public async Task RunInteractive(string messageId, ProcessingStage stage, CancellationToken cancellationToken)
#pragma warning restore PS0003
    {
        running = true;
        try
        {
            await userInterface.SendEvent(new StageProgress(messageId, stage, 0));

            for (var i = 0; i <= 100; i++)
            {
                if (failureTriggered)
                {
                    failureTriggered = false;
                    throw new Exception("Simulated failure");
                }

                await userInterface.SendEvent(new StageProgress(messageId, stage, i));
                await Task.Delay(25, cancellationToken).ConfigureAwait(false);
            }

            await userInterface.SendEvent(new StageCompleted(messageId, stage));
        }
        finally
        {
            running = false;
        }
        
    }

    public void Trigger()
    {
        //HINT: Trigger failure only if currently running in interactive mode
        if (running)
        {
            failureTriggered = true;
        }
    }
}