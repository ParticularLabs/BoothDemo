using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;

/// <summary>
/// Reads queue lengths from the RabbitMQ management API.
/// </summary>
sealed class RabbitMQManagementQueueLengthProvider : IQueueLengthProvider, IDisposable
{
    readonly HttpClient client;
    readonly string virtualHost;

    public RabbitMQManagementQueueLengthProvider(string managementUrl, string username = "guest", string password = "guest", string virtualHost = "/")
    {
        this.virtualHost = Uri.EscapeDataString(virtualHost);

        client = new HttpClient { BaseAddress = new Uri(managementUrl) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}")));
    }

    public async Task<long> GetQueueLength(string queueName, CancellationToken cancellationToken = default)
    {
        var queueInfo = await client.GetFromJsonAsync<QueueInfo>($"api/queues/{virtualHost}/{Uri.EscapeDataString(queueName)}", cancellationToken);
        return queueInfo?.Messages ?? 0;
    }

    public void Dispose() => client.Dispose();

    class QueueInfo
    {
        [JsonPropertyName("messages")]
        public long Messages { get; set; }
    }
}
