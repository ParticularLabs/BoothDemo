using NServiceBus;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;

namespace Shared;

public class UserInterface
{
    List<IControl> controls = [];

    Channel<UiEvent> uiEventChannel = Channel.CreateUnbounded<UiEvent>();

    public ValueTask SendEvent(UiEvent uiEvent)
    {
        return uiEventChannel.Writer.WriteAsync(uiEvent);
    }

    public void BindDial(char upKey, char downKey, string name, Func<string> getState, Action<int> action)
    {
        controls.Add(new DialControl(upKey, downKey, name, getState, action));
    }

    public void BindToggle(char toggleKey, string name, Action enableAction, Action disableAction)
    {
        controls.Add(new ToggleControl(toggleKey, name, enableAction, disableAction));
    }

    public void BindButton(char buttonKey, string name, Action pressedAction)
    {
        controls.Add(new ButtonControl(buttonKey, name, pressedAction));
    }

#pragma warning disable PS0018
    public Task RunLoop(string title)
#pragma warning restore PS0018
    {
        Console.Title = title;

        CancellationTokenSource quitTokenSource = new CancellationTokenSource();

        var keyboardLoop = Task.Run(async () =>
        {
            while (true)
            {
                int? param = null;
                var key1 = Console.ReadKey(true);
                if (key1.Key == ConsoleKey.Escape)
                {
                    await quitTokenSource.CancelAsync();
                    return;
                }

                if (char.IsUpper(key1.KeyChar))
                {
                    //Upper case character, read another character as param
                    var key2 = Console.ReadKey(true);
                    if (int.TryParse(new string(key2.KeyChar, 1), out var p))
                    {
                        param = p;
                    }
                }

                string? log = null;
                var matchedControl = controls.FirstOrDefault(x => x.Match(key1.KeyChar, param, out log));
                if (matchedControl != null && log != null)
                {
                    await uiEventChannel.Writer.WriteAsync(new ControlStateChanged(log), quitTokenSource.Token);
                }
            }
        });

        return AnsiConsole.Progress()
            .AutoClear(true)
            .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),    // Task description
                    new ProgressBarColumn(),        // Progress bar
                    new PercentageColumn(),         // Percentage
                    new ElapsedTimeColumn(),      // Remaining time
                    new SpinnerColumn(),            // Spinner
                })
            .UseRenderHook((renderable, tasks) => RenderHook(tasks, renderable))
            .StartAsync(async ctx =>
            {
                var processingTasks = new Dictionary<(string MessageId, ProcessingStage Stage), ProgressTask>();
                var sendingTasks = new ProgressTask[3];
                int currentSendingTask = 2;

                while (!quitTokenSource.IsCancellationRequested)
                {
                    while (uiEventChannel.Reader.TryRead(out var evt))
                    {
                        switch (evt)
                        {
                            case ProcessingStarted started:

                                var processingTask = ctx.AddTask($"Processing {started.MessageId}", maxValue: 100, autoStart: false).IsIndeterminate();
                                var dispatchingTask = ctx.AddTask($"Dispatching {started.MessageId}", maxValue: 100, autoStart: false).IsIndeterminate();
                                var ackTask = ctx.AddTask($"Acknowledging {started.MessageId}", maxValue: 100, autoStart: false).IsIndeterminate();

                                processingTasks[(started.MessageId, ProcessingStage.Processing)] = processingTask;
                                processingTasks[(started.MessageId, ProcessingStage.Dispatching)] = dispatchingTask;
                                processingTasks[(started.MessageId, ProcessingStage.Acknowledging)] = ackTask;
                                break;

                            case StageProgress sp:
                                var task = processingTasks[(sp.MessageId, sp.Stage)];
                                if (!task.IsStarted)
                                {
                                    task.StartTask();
                                }

                                task.Value = sp.Percent;
                                task.IsIndeterminate = false;
                                break;

                            case StageCompleted sc:
                                task = processingTasks[(sc.MessageId, sc.Stage)];
                                task.Value = 100;
                                task.StopTask();
                                AnsiConsole.MarkupLine($"[grey]{sc.Stage} done for {sc.MessageId}[/]");
                                break;

                            case ProcessingCompleted tc:
                                processingTask = processingTasks[(tc.MessageId, ProcessingStage.Processing)];
                                dispatchingTask = processingTasks[(tc.MessageId, ProcessingStage.Dispatching)];
                                ackTask = processingTasks[(tc.MessageId, ProcessingStage.Acknowledging)];

                                ctx.RemoveTask(processingTask);
                                ctx.RemoveTask(dispatchingTask);
                                ctx.RemoveTask(ackTask);

                                AnsiConsole.MarkupLine($"[green]Done processing message {tc.MessageId}[/]");
                                break;

                            case ProcessingFailed tf:

                                if (processingTasks.Remove((tf.MessageId, ProcessingStage.Processing), out processingTask))
                                {
                                    ctx.RemoveTask(processingTask);
                                }
                                if (processingTasks.Remove((tf.MessageId, ProcessingStage.Dispatching), out dispatchingTask))
                                {
                                    ctx.RemoveTask(dispatchingTask);
                                }
                                if (processingTasks.Remove((tf.MessageId, ProcessingStage.Acknowledging), out ackTask))
                                {
                                    ctx.RemoveTask(ackTask);
                                }

                                AnsiConsole.MarkupLine($"[red]Error processing message {tf.MessageId}[/]");
                                break;

                            case ControlStateChanged csc:
                                AnsiConsole.MarkupLine($"[grey]{csc.Text}[/]");
                                break;

                            case SendingStarted ss:
                                currentSendingTask = (currentSendingTask + 1) % sendingTasks.Length;
                                if (sendingTasks[currentSendingTask] != null)
                                {
                                    ctx.RemoveTask(sendingTasks[currentSendingTask]);
                                }
                                var sendingTask = ctx.AddTask($"Sending batch #{ss.BatchNumber} of {ss.BatchSize} msg", maxValue: 100, autoStart: true);
                                sendingTasks[currentSendingTask] = sendingTask;
                                
                                break;

                            case SendingProgress sp:
                                if (sendingTasks[currentSendingTask] != null)
                                {
                                    sendingTasks[currentSendingTask].Value = sp.Percent;
                                }
                                break;

                            case SendingCompleted sc:
                                if (sendingTasks[currentSendingTask] != null)
                                {
                                    sendingTasks[currentSendingTask].Value = 100;
                                    sendingTasks[currentSendingTask].StopTask();
                                }
                                break;

                            case OrderSagaStarted oss:
                                AnsiConsole.MarkupLine($"[yellow]Saga started for order {oss.OrderId}[/]");
                                break;

                            case OrderSagaCompleted osc:
                                AnsiConsole.MarkupLine($"[yellow]Saga completed for order {osc.OrderId}[/]");
                                break;

                        }
                    }
                    await Task.Delay(50, quitTokenSource.Token);
                }
            });
    }

    IRenderable RenderHook(IReadOnlyList<ProgressTask> tasks, IRenderable renderable)
    {
        var content = new List<Renderable>();
        foreach (var ctrl in controls)
        {
            content.Add(ctrl.ReportState());
        }

        var header = new Panel(new Rows(content)).Expand().RoundedBorder();
        
        return new Rows(header, renderable);
    }
}
