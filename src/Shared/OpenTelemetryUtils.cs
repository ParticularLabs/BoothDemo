using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Shared;

public static class OpenTelemetryUtils
{
    // Standard OpenTelemetry variable read by the OTLP exporter. When set (to the booth's OpenTelemetry Collector),
    // traces, metrics and logs are exported over OTLP. OTEL_EXPORTER_OTLP_PROTOCOL and OTEL_EXPORTER_OTLP_HEADERS are honored too.
    const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    // The OpenTelemetry default is 60 seconds, too slow for live graphs at the booth
    const int MetricExportIntervalMilliseconds = 5000;

    static bool OtlpEnabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OtlpEndpointVariable));

    public static IDisposable? ConfigureOpenTelemetry(string name, string id)
    {
        if (!OtlpEnabled)
        {
            return null;
        }

        var meterProvider = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(CreateResource(name, id))
            .AddMeter("NServiceBus.Core*")
            .AddOtlpExporter((_, readerOptions) =>
                readerOptions.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = MetricExportIntervalMilliseconds)
            .Build();

        var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(CreateResource(name, id))
            .AddSource("NServiceBus.Core*")
            .AddOtlpExporter()
            .Build();

        return new CompositeDisposable(meterProvider, tracerProvider);
    }

    /// <summary>
    /// Exports logs over OTLP when the OTLP endpoint is configured, so they show up as structured logs in the Aspire dashboard.
    /// </summary>
    public static ILoggingBuilder AddOtlpLogging(this ILoggingBuilder builder, string name, string id)
    {
        if (OtlpEnabled)
        {
            builder.AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(CreateResource(name, id));
                options.IncludeFormattedMessage = true;
                options.AddOtlpExporter();
            });
        }

        return builder;
    }

    static ResourceBuilder CreateResource(string name, string id) =>
        ResourceBuilder.CreateDefault().AddService(serviceName: name, serviceInstanceId: id);

    sealed class CompositeDisposable(params IDisposable?[] disposables) : IDisposable
    {
        public void Dispose()
        {
            foreach (var disposable in disposables)
            {
                disposable?.Dispose();
            }
        }
    }
}
