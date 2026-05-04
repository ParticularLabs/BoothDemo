using NServiceBus.Metrics.ServiceControl;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

class NativeQueueLengthReporter
{
    IReportNativeQueueLength nativeQueueLengthReporter;
    readonly string[] queues;

    public NativeQueueLengthReporter(IReportNativeQueueLength nativeQueueLengthReporter, string[] queues)
    {
        this.nativeQueueLengthReporter = nativeQueueLengthReporter;
        this.queues = queues;
    }

#pragma warning disable PS0018
    public async Task ReportNativeQueueLength()
#pragma warning restore PS0018
    {
        foreach (var monitoredQueue in queues)
        {
            var count = await GetQueueLength(monitoredQueue);
            nativeQueueLengthReporter.ReportQueueLength(monitoredQueue, count);
        }
    }

#pragma warning disable PS0018
    static async Task<int> GetQueueLength(string queueName)
#pragma warning restore PS0018
    {
        var host = "http://rabbitmq:15672";
        var vhost = "%2F"; // "/" encoded

        var username = "guest";
        var password = "guest";

        var url = $"{host}/api/queues/{vhost}/{queueName}";

        using var client = new HttpClient();

        var authToken = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{username}:{password}")
        );

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", authToken);

        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();

        var queueInfo = JsonSerializer.Deserialize<QueueInfo>(content);

        var queueLength = queueInfo?.Messages ?? 0;

        return queueLength;
    }
}

public class QueueInfo
{
    [JsonPropertyName("messages")]
    public int Messages { get; set; }
}