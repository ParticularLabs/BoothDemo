using Messages;
using Shared;

namespace ClientUI;

class SimulatedCustomers(IEndpointInstance endpointInstance, UserInterface ui)
{
    const int batchSize = 5;

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

    private int EffectiveRate => Math.Max(blackFriday ? 32 : NoiseModifiedRate, 0);
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
            OrderId = Guid.NewGuid().ToString()
        };

        var messageId = MessageIdHelper.GetHumanReadableMessageId();

        await SendOneMessage(messageId, cancellationToken, placeOrderCommand);

        if (Random.Shared.Next(10) < duplicateLikelihood)
        {
            //Send a duplicate
            await SendOneMessage(messageId, cancellationToken, placeOrderCommand);
        }
    }

    private async Task SendOneMessage(string messageId, CancellationToken cancellationToken, PlaceOrder placeOrderCommand)
    {
        var sendOptions = new SendOptions();

        if (rate == 0) //Manual mode
        {
            sendOptions.SetHeader("MonitoringDemo.ManualMode", "True");
        }

        sendOptions.SetMessageId(messageId);
        await endpointInstance.Send(placeOrderCommand, sendOptions, cancellationToken);
    }

    DateTime nextReset;
    int currentIntervalCount;
    int rate = 0;
    private int noiseComponent = 0;
    int noiseFactor = 0;
    private int duplicateLikelihood;
    private bool blackFriday;
    private SemaphoreSlim manualModeSemaphore = new SemaphoreSlim(0);
}