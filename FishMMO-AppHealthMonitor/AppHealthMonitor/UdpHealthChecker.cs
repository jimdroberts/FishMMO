using System.Net.Sockets;
using System.Text;
using FishMMO.Logging;

namespace AppHealthMonitor
{
	/// <summary>
	/// Checks port health by attempting to send a UDP datagram.
	/// Note: UDP is connectionless, so a successful send only confirms the socket is functional,
	/// not that the remote endpoint is listening.
	/// </summary>
	public sealed class UdpHealthChecker : IHealthChecker
	{
		/// <summary>
		/// Pre-encoded health check datagram payload to avoid repeated allocations.
		/// </summary>
		private static readonly byte[] HealthCheckPayload = Encoding.UTF8.GetBytes("healthcheckping");

		/// <inheritdoc />
		public PortType PortType => PortType.UDP;

		/// <inheritdoc />
		public async Task<bool> IsResponsiveAsync(string host, int port, int timeoutMilliseconds, CancellationToken cancellationToken)
		{
			using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeoutCts.CancelAfter(timeoutMilliseconds);

			using var client = new UdpClient();
			try
			{
				await client.SendAsync(HealthCheckPayload, host, port, timeoutCts.Token);
				return true;
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				Log.Warning("UdpHealthCheck", $"UDP Port Send Timeout: {host}:{port} did not complete send within {timeoutMilliseconds}ms.");
				return false;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (SocketException ex)
			{
				Log.Warning("UdpHealthCheck", $"UDP Port Check Error: Could not send to {host}:{port}. Socket error: {ex.SocketErrorCode} - {ex.Message}");
				return false;
			}
			catch (Exception ex)
			{
				Log.Error("UdpHealthCheck", $"UDP Port Check Unexpected Error: {ex.Message}", ex);
				return false;
			}
		}
	}
}