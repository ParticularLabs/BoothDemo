using Messages;
using Shared;

namespace ClientUI;

class SimulatedCustomers(UserInterface ui)
{
    const int batchSize = 5;

    // A send to a broker that went away silently (unplugged cable) waits for the publisher confirm until the connection
    // is declared dead. Give up sooner so that the UI stays responsive and reports the failure.
    static readonly TimeSpan sendTimeout = TimeSpan.FromSeconds(3);

    public void Connected(IEndpointInstance instance) => endpointInstance = instance;

    public void Disconnected() => endpointInstance = null;

    public void BindSendingRateDial(char upKey, char downKey)
    {
        ui.BindDial(upKey, downKey,
            "Sending rate",
            () => $"{rate}/s", x =>
            {
                rate = x;
                if (x > 0)
                {
                    manualModeSemaphore.Release();
                }
            });
    }

    public void BindDuplicateLikelihoodDial(char upKey, char downKey)
    {
        ui.BindDial(upKey, downKey,
            "Duplicate message rate",
            () => $"{duplicateLikelihood * 10}%", x => duplicateLikelihood = x);
    }

    public void BindNoiseLevelDial(char upKey, char downKey)
    {
        ui.BindDial(upKey, downKey,
            "Noise level",
            () => $"{noiseFactor}", x => noiseFactor = x);
    }

    public void BindManualSendButton(char key)
    {
        ui.BindButton(key, "Sending a message", () => manualModeSemaphore.Release());
    }

    public void BindRaffleMode(char key)
    {
        ui.BindToggle(key, "Raffle mode", () => raffleMode = true, () => raffleMode = false);
    }

    int EffectiveRate => Math.Max(NoiseModifiedRate, 0);
    int NoiseModifiedRate => rate * batchSize + noiseComponent;

    public async Task Run(CancellationToken cancellationToken = default)
    {
        nextReset = DateTime.UtcNow.AddSeconds(1);
        currentIntervalCount = 0;
        var noiseFatigue = 0;
        var batchNumber = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            if (rate == 0)
            {
                await ui.SendEvent(new SendingCompleted());
                await manualModeSemaphore.WaitAsync(cancellationToken);
                await PlaceSingleOrder(cancellationToken);
            }
            else
            {
                var now = DateTime.UtcNow;
                if (now > nextReset)
                {
                    await ui.SendEvent(new SendingCompleted());

                    //HINT: Re-calculate rate
                    currentIntervalCount = 0;

                    //Likelihood of increasing the noise component decreases with the absolute value of the noise (base is 50%)
                    var noiseIncrease =
                        Random.Shared.Next(Math.Abs(noiseComponent) + (9 - noiseFactor) / 2 + noiseFatigue / 30) == 0;
                    if (noiseIncrease)
                    {
                        if (noiseComponent == 0)
                        {
                            noiseComponent += Random.Shared.Next(2) == 0 ? 1 : -1;
                        }
                        else
                        {
                            noiseComponent += Math.Sign(noiseComponent);
                        }
                    }
                    else if (noiseComponent != 0)
                    {
                        noiseComponent -= Math.Sign(noiseComponent);
                    }
                    else
                    {
                        noiseFatigue = 0;
                    }

                    noiseFatigue += Math.Abs(noiseComponent);

                    nextReset = now.AddSeconds(batchSize);

                    batchNumber++;
                    await ui.SendEvent(new SendingStarted(batchNumber, NoiseModifiedRate));
                }


                await PlaceSingleOrder(cancellationToken);
                currentIntervalCount++;

                await ui.SendEvent(new SendingProgress(currentIntervalCount * 100 / (double)EffectiveRate));

                //HINT: Wait a bit
                try
                {
                    var messagesLeft = EffectiveRate - currentIntervalCount;
                    if (messagesLeft > 0)
                    {
                        var delay = (nextReset - DateTime.UtcNow) / messagesLeft;
                        if (delay > TimeSpan.Zero)
                        {
                            await Task.Delay(delay, cancellationToken);
                        }
                    }
                    else
                    {
                        var delay = nextReset - DateTime.UtcNow;
                        if (delay > TimeSpan.Zero)
                        {
                            await Task.Delay(delay, cancellationToken);
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }

    async Task PlaceSingleOrder(CancellationToken cancellationToken)
    {
        var placeOrderCommand = new PlaceOrder
        {
            OrderId = Guid.NewGuid().ToString(),
            Campaign = raffleMode ? "Techorama2026" : "Website"
        };

        var messageId = MessageIdHelper.GetHumanReadableMessageId();

        if (!await TrySendOneMessage(messageId, placeOrderCommand, cancellationToken))
        {
            return;
        }

        if (Random.Shared.Next(10) < duplicateLikelihood)
        {
            //Send a duplicate
            await TrySendOneMessage(messageId, placeOrderCommand, cancellationToken);
        }
    }

    // Pretends to be a web API in front of the endpoint: a message that can't be sent is reported as an HTTP 500 response
    // and the simulation carries on, so the demo keeps running while the broker is unreachable.
    async Task<bool> TrySendOneMessage(string messageId, PlaceOrder placeOrderCommand, CancellationToken cancellationToken)
    {
        var failure = await Send(messageId, placeOrderCommand, cancellationToken);

        if (failure is not null)
        {
            failing = true;
            await ui.SendEvent(new SendingFailed(messageId, failure));
            return false;
        }

        if (failing)
        {
            failing = false;
            await ui.SendEvent(new SendingRecovered());
        }

        return true;
    }

    // Returns why the message could not be sent, or null when it was sent
    async Task<string?> Send(string messageId, PlaceOrder placeOrderCommand, CancellationToken cancellationToken)
    {
        var instance = endpointInstance;
        if (instance is null)
        {
            return "not connected to the message broker";
        }

        var sendOptions = new SendOptions();

        if (rate == 0) //Manual mode
        {
            sendOptions.SetHeader("MonitoringDemo.ManualMode", "True");
        }

        sendOptions.SetMessageId(messageId);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(sendTimeout);

        try
        {
            await instance.Send(placeOrderCommand, sendOptions, timeout.Token);
            return null;
        }
        catch (OperationCanceledException) when (timeout.Token.IsCancellationRequested)
        {
            // Shutting down
            cancellationToken.ThrowIfCancellationRequested();

            return "timed out waiting for the message broker";
        }
        catch (Exception ex)
        {
            return $"connection to the message broker lost ({ex.GetType().Name})";
        }
    }

    volatile IEndpointInstance? endpointInstance;
    bool failing;
    DateTime nextReset;
    int currentIntervalCount;
    int rate = 0;
    bool raffleMode;
    private int noiseComponent = 0;
    int noiseFactor = 0;
    private int duplicateLikelihood;
    private SemaphoreSlim manualModeSemaphore = new SemaphoreSlim(0);
}