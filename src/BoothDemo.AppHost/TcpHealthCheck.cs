using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;

sealed class TcpHealthCheck(string host, int port) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, cancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (SocketException ex)
        {
            return HealthCheckResult.Unhealthy($"Cannot connect to {host}:{port}", ex);
        }
        catch (TimeoutException ex)
        {
            return HealthCheckResult.Unhealthy($"Cannot connect to {host}:{port}", ex);
        }
    }

    public static (string Host, int Port) ParseAddress(string address)
    {
        var separator = address.LastIndexOf(':');
        return (address[..separator], int.Parse(address[(separator + 1)..]));
    }
}
