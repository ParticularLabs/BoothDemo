using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NServiceBus;
using NServiceBus.Features;
using NServiceBus.Logging;
using NServiceBus.Metrics.ServiceControl;
using NServiceBus.Transport;

class ReportNativeQueueLength : Feature
{
    static readonly ILog Log = LogManager.GetLogger<PeriodicallyReportQueueLength>();

    public ReportNativeQueueLength()
    {
        EnableByDefault();
        DependsOn("NServiceBus.Metrics.ServiceControl.ReportingFeature");
    }

    protected override void Setup(FeatureConfigurationContext context)
    {
        var queueNames = new[] { "Sales" };

        context.Services.AddSingleton<NativeQueueLengthReporter>(x => new NativeQueueLengthReporter(x.GetRequiredService<IReportNativeQueueLength>(), queueNames));
        context.Services.AddSingleton<PeriodicallyReportQueueLength>();

        context.RegisterStartupTask(b => new PeriodicallyReportQueueLength(b.GetRequiredService<NativeQueueLengthReporter>()));
    }

    class PeriodicallyReportQueueLength : FeatureStartupTask
    {
        readonly NativeQueueLengthReporter reporter;

        TimeSpan delayBetweenReports = TimeSpan.FromSeconds(5);
        CancellationTokenSource cancellationTokenSource;
        Task task;

        public PeriodicallyReportQueueLength(NativeQueueLengthReporter reporter) => this.reporter = reporter;

        protected override Task OnStart(IMessageSession messageSession, CancellationToken cancellationToken = default)
        {
            cancellationTokenSource = new CancellationTokenSource();

            task = Task.Run(async () =>
            {
                while (!cancellationTokenSource.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(delayBetweenReports, cancellationTokenSource.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        // private token, reporting is being stopped, don't log the exception because the stack trace of Task.Delay is not interesting
                        break;
                    }

                    try
                    {
                        await reporter.ReportNativeQueueLength();
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Error reporting MSMQ native queue length", ex);
                    }
                }
            },
                CancellationToken.None);

            return Task.CompletedTask;
        }

        protected override Task OnStop(IMessageSession messageSession, CancellationToken cancellationToken = default)
        {
            cancellationTokenSource.Cancel();

            return task;
        }
    }
}