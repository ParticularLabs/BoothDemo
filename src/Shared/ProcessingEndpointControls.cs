using Microsoft.Extensions.DependencyInjection;

namespace Shared;

public class ProcessingEndpointControls(Func<EndpointConfiguration> endpointConfigProvider, UserInterface ui)
{
    private IEndpointInstance? runningEndpoint;

    private bool delayedRetries;
    private bool autoThrottle;
    private volatile bool rateLimited;

    private readonly AcknowledgingMessageProgressBehavior acknowledgingMessageProgressBehavior = new AcknowledgingMessageProgressBehavior(ui);
    private readonly ProcessingMessageProgressBehavior processingMessageProgressBehavior = new ProcessingMessageProgressBehavior(ui);
    private readonly DispatchingProgressBehavior dispatchingMessageProgressBehavior = new DispatchingProgressBehavior(ui);
    private readonly SlowProcessingSimulationBehavior slowProcessingSimulationBehavior = new SlowProcessingSimulationBehavior();
    private readonly DatabaseFailureSimulationBehavior databaseFailureSimulationBehavior = new DatabaseFailureSimulationBehavior();
    private readonly DatabaseDownSimulationBehavior databaseDownSimulationBehavior = new DatabaseDownSimulationBehavior();
    private CancellationTokenSource? stopTokenSource;
    private readonly SemaphoreSlim restartSemaphore = new SemaphoreSlim(1);
    private Task? restartTask;

    void Register(EndpointConfiguration endpointConfiguration)
    {
        endpointConfiguration.RegisterComponents(sc =>
        {
            sc.AddSingleton(ui);
        });
        endpointConfiguration.Pipeline.Register(acknowledgingMessageProgressBehavior, "Shows progress of retrieving messages");
        endpointConfiguration.Pipeline.Register(processingMessageProgressBehavior, "Shows progress of processing messages");
        endpointConfiguration.Pipeline.Register(dispatchingMessageProgressBehavior, "Shows progress of dispatching messages");
        endpointConfiguration.Pipeline.Register(slowProcessingSimulationBehavior, "Simulates slow processing");
        endpointConfiguration.Pipeline.Register(databaseFailureSimulationBehavior, "Simulates faulty database");
        endpointConfiguration.Pipeline.Register(databaseDownSimulationBehavior, "Simulates down database");
        endpointConfiguration.Pipeline.Register(new PropagateManualModeBehavior(), "Propagates manual mode settings");
    }

    public void Start()
    {
        stopTokenSource = new CancellationTokenSource();
        restartTask = Task.Run(async () =>
        {
            var stopToken = stopTokenSource.Token;
            while (!stopToken.IsCancellationRequested)
            {
                try
                {
                    await restartSemaphore.WaitAsync(stopToken);
                    //await Task.Delay(5000);
                    await RestartEndpoint();
                }
                catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
                {
                    break;
                }
#pragma warning disable PS0019
                catch (Exception e)
#pragma warning restore PS0019
                {
                    Console.WriteLine(e);
                }
            }
        });
    }

#pragma warning disable PS0018
    async Task RestartEndpoint()
#pragma warning restore PS0018
    {
        if (runningEndpoint != null)
        {
            await runningEndpoint.Stop();
        }

        var config = endpointConfigProvider();

        if (!delayedRetries)
        {
            config.Recoverability().Delayed(settings => settings.NumberOfRetries(0));
        }

        // The new endpoint starts at full concurrency
        if (rateLimited)
        {
            rateLimited = false;
            await ui.SendEvent(new RateLimitingChanged(false));
        }

        if (autoThrottle)
        {
            //HINT: After 5 consecutive failures the endpoint switches to rate limited mode: it processes one message at a
            //HINT: time and waits a second after each failure, until a message succeeds again.
            var rateLimitSettings = new RateLimitSettings(
                onRateLimitStarted: _ =>
                {
                    rateLimited = true;
                    return ui.SendEvent(new RateLimitingChanged(true)).AsTask();
                },
                onRateLimitEnded: _ =>
                {
                    rateLimited = false;
                    return ui.SendEvent(new RateLimitingChanged(false)).AsTask();
                });

            //HINT: Rate limiting alone doesn't change what happens to a failed message: it would still go through the
            //HINT: immediate and delayed retries to the error queue. While rate limited, the failure is most likely caused
            //HINT: by something outside the message (a database that is down), so keep retrying it instead. The message
            //HINT: stays at the head of the queue, and the ones behind it wait.
            config.Recoverability().CustomPolicy((recoverabilityConfig, errorContext) =>
            {
                if (rateLimited && !IsUnrecoverable(recoverabilityConfig, errorContext.Exception))
                {
                    return RecoverabilityAction.ImmediateRetry();
                }

                return DefaultRecoverabilityPolicy.Invoke(recoverabilityConfig, errorContext);
            });
            config.Recoverability().OnConsecutiveFailures(5, rateLimitSettings);
        }

        Register(config);

        runningEndpoint = await Endpoint.Start(config);
    }

    // A message that can never succeed would otherwise be retried forever and block the queue
    static bool IsUnrecoverable(RecoverabilityConfig recoverabilityConfig, Exception exception) =>
        recoverabilityConfig.Failed.UnrecoverableExceptionTypes.Any(type => type.IsInstanceOfType(exception));

#pragma warning disable PS0018
    public async Task StopEndpoint()
#pragma warning restore PS0018
    {
        stopTokenSource?.Cancel();
        if (restartTask != null)
        {
            await restartTask;
        }
        if (runningEndpoint != null)
        {
            await runningEndpoint.Stop();
        }
    }

    public void BindProcessingTimeDial(char upKey, char downKey)
    {
        ui.BindDial(upKey, downKey,
            "processing time",
            () => slowProcessingSimulationBehavior.ReportState(),
            x => slowProcessingSimulationBehavior.SetProcessingDelay(x));
    }

    public void BindSimulatedFailuresDial(char upKey, char downKey)
    {
        ui.BindDial(upKey, downKey, "failure rate",
            () => databaseFailureSimulationBehavior.ReportState(),
            x => databaseFailureSimulationBehavior.SetFailureLevel(x));
    }

    public void BindDatabaseDownSimulationToggle(char toggleKey)
    {
        ui.BindToggle(toggleKey, "database maintenance",
            () => databaseDownSimulationBehavior.Down(),
            () => databaseDownSimulationBehavior.Up());
    }

    public void BindDelayedRetriesToggle(char toggleKey)
    {
        ui.BindToggle(toggleKey, "delayed retries",
            () =>
            {
                delayedRetries = true;
                restartSemaphore.Release();
            },
            () =>
            {
                delayedRetries = false;
                restartSemaphore.Release();
            });
    }

    public void BindAutoThrottleToggle(char toggleKey)
    {
        ui.BindToggle(toggleKey, "auto rate limiting",
            () =>
            {
                autoThrottle = true;
                restartSemaphore.Release();
            },
            () =>
            {
                autoThrottle = false;
                restartSemaphore.Release();
            });
        ui.BindInfo("Rate limiting", () => rateLimited ? "ACTIVE, one message at a time" : null);
    }

    public void BindFailureProcessingButton(UserInterface userInterface, char key)
    {
        userInterface.BindButton(key, "intermittent failure", () =>
        {
            processingMessageProgressBehavior.Failure();
            dispatchingMessageProgressBehavior.Failure();
            acknowledgingMessageProgressBehavior.Failure();
        });
    }

}