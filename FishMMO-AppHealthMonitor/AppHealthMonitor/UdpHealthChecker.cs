using System.Net.Sockets;
using System.Text;
using FishMMO.Logging;

namespace AppHealthMonitor
{
	/// <summary>
	/// Checks port health by attempting to send a UDP datagram.
	/// <para>
	/// <strong>Limitation:</strong> UDP is connectionless. A successful send only confirms the local
	/// OS accepted the datagram into the send buffer, not that the remote endpoint received it or is
	/// alive. This checker will report success even if the target application is down. For reliable
	/// health checking, prefer TCP or WebSocket. Use UDP checks only as a supplementary probe alongside
	/// other port types.
	/// </para>
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
			ArgumentException.ThrowIfNullOrWhiteSpace(host);

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