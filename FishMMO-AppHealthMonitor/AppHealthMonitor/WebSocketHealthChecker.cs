using System.Net.WebSockets;
using FishMMO.Logging;

namespace AppHealthMonitor
{
	/// <summary>
	/// Checks port health by attempting to establish a WebSocket connection.
	/// Verifies the endpoint is functional by confirming the connection reaches <see cref="WebSocketState.Open"/>.
	/// The close handshake is intentionally skipped; the <c>using</c> block disposes the socket,
	/// which is faster and equally valid for health probing.
	/// </summary>
	public sealed class WebSocketHealthChecker : IHealthChecker
	{
		/// <inheritdoc />
		public PortType PortType => PortType.WebSocket;

		/// <inheritdoc />
		public async Task<bool> IsResponsiveAsync(string host, int port, int timeoutMilliseconds, CancellationToken cancellationToken)
		{
			using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeoutCts.CancelAfter(timeoutMilliseconds);

			using var ws = new ClientWebSocket();
			ws.Options.KeepAliveInterval = TimeSpan.Zero;

			try
			{
				string formattedHost = host.Contains(':') ? $"[{host}]" : host;
				var uri = new Uri($"ws://{formattedHost}:{port}");
				await ws.ConnectAsync(uri, timeoutCts.Token);

				if (ws.State == WebSocketState.Open)
				{
					return true;
				}

				Log.Warning("WebSocketHealthCheck", $"WebSocket Port Check: Connection failed. State: {ws.State}.");
				return false;
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				Log.Warning("WebSocketHealthCheck", $"WebSocket Port Check Timeout: ws://{host}:{port} did not establish connection within {timeoutMilliseconds}ms.");
				return false;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (WebSocketException ex)
			{
				Log.Warning("WebSocketHealthCheck", $"WebSocket Port Check Error: ws://{host}:{port}. WebSocket error: {ex.WebSocketErrorCode} - {ex.Message}");
				return false;
			}
			catch (Exception ex)
			{
				Log.Error("WebSocketHealthCheck", $"WebSocket Port Check Unexpected Error: {ex.Message}", ex);
				return false;
			}
		}
	}
}