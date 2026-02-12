using System.Net.Sockets;
using FishMMO.Logging;

namespace AppHealthMonitor
{
	/// <summary>
	/// Checks port health by attempting to establish a TCP connection.
	/// Uses a linked cancellation token to enforce timeouts without leaking timers.
	/// </summary>
	public sealed class TcpHealthChecker : IHealthChecker
	{
		/// <inheritdoc />
		public PortType PortType => PortType.TCP;

		/// <inheritdoc />
		public async Task<bool> IsResponsiveAsync(string host, int port, int timeoutMilliseconds, CancellationToken cancellationToken)
		{
			using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeoutCts.CancelAfter(timeoutMilliseconds);

			using var client = new TcpClient();
			try
			{
				await client.ConnectAsync(host, port, timeoutCts.Token);
				return true;
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				Log.Warning("TcpHealthCheck", $"TCP Port Check Timeout: {host}:{port} did not respond within {timeoutMilliseconds}ms.");
				return false;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (SocketException ex)
			{
				Log.Warning("TcpHealthCheck", $"TCP Port Check Error: Could not connect to {host}:{port}. Socket error: {ex.SocketErrorCode} - {ex.Message}");
				return false;
			}
			catch (Exception ex)
			{
				Log.Error("TcpHealthCheck", $"TCP Port Check Unexpected Error: {ex.Message}", ex);
				return false;
			}
		}
	}
}