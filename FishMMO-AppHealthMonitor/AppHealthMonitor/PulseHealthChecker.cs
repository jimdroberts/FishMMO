using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;

namespace AppHealthMonitor
{
	/// <summary>
	/// Checks a FishMMO game server by reading the pulse it writes to the database.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The transport is WebTransport over QUIC, which is UDP only. There is no TCP listener on
	/// a game port, so a TCP check fails against a perfectly healthy server and the supervisor
	/// restarts it until it gives up — and a UDP check cannot replace it, because a UDP send
	/// succeeds whether or not anything is listening. QUIC has no cheap "is the port open"
	/// answer either: the server replies only to a valid Initial packet carrying a TLS
	/// ClientHello, so a real probe means performing a handshake with the native msquic library
	/// on every host.
	/// </para>
	/// <para>
	/// The pulse avoids all of that and proves strictly more. A socket probe shows a socket is
	/// bound; the pulse shows the server is running its loop, doing work, and can still reach
	/// the database.
	/// </para>
	/// </remarks>
	public sealed class PulseHealthChecker : IHealthChecker
	{
		/// <summary>How long a database round trip is allowed before it counts as no answer.</summary>
		public const int DefaultTimeoutMs = 5000;

		private readonly IServerBoardService board;
		private readonly string tier;
		private readonly string serverName;
		private readonly int staleSeconds;
		private readonly string logSource;

		/// <summary>Creates a checker for one registered server.</summary>
		/// <param name="board">The read service.</param>
		/// <param name="tier">login, world or scene.</param>
		/// <param name="serverName">The name the server registers under, from its own configuration.</param>
		/// <param name="staleSeconds">How old a pulse may be before the server counts as down.</param>
		public PulseHealthChecker(IServerBoardService board, string tier, string serverName, int staleSeconds)
		{
			this.board = board;
			this.tier = tier;
			this.serverName = serverName;
			this.staleSeconds = staleSeconds;
			logSource = $"PulseHealthCheck:{serverName}";
		}

		/// <inheritdoc/>
		public PortType PortType => PortType.DatabasePulse;

		/// <summary>
		/// Reports whether the server's pulse is recent.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>This check fails OPEN.</b> If the database cannot be read, it returns healthy and
		/// logs loudly. That is deliberate and it is the most important decision in this file.
		/// </para>
		/// <para>
		/// Treating an unreadable database as "unhealthy" would mean that during a database
		/// outage every pulse looks stale at once, on every host, and every daemon restarts
		/// every server simultaneously — turning a recoverable outage into a shard-wide one, at
		/// the exact moment nobody can afford it. The distinction that matters is between
		/// <em>knowing</em> a server is silent and <em>not knowing anything</em>, and only the
		/// first is grounds for killing a process.
		/// </para>
		/// <para>
		/// The cost is real and worth stating: during a database outage a genuinely dead server
		/// will not be caught. That is the better half of the trade.
		/// </para>
		/// <para>
		/// The <paramref name="host"/> and <paramref name="port"/> arguments are ignored. This
		/// check does not reach the network, which is the entire point of it.
		/// </para>
		/// </remarks>
		public async Task<bool> IsResponsiveAsync(string host, int port, int timeoutMilliseconds, CancellationToken cancellationToken)
		{
			using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			timeoutCts.CancelAfter(timeoutMilliseconds > 0 ? timeoutMilliseconds : DefaultTimeoutMs);

			try
			{
				var result = await board.FetchLastPulseAsync(tier, serverName, timeoutCts.Token);

				if (!result.IsSuccess)
				{
					Log.Error(logSource,
						$"Could not read the pulse for '{serverName}': [{result.ErrorCode}] {result.ErrorMessage}. " +
						"Reporting healthy so a database problem does not restart every server at once.");
					return true;
				}

				DateTime? lastPulse = result.Data;
				if (lastPulse == null)
				{
					/* No row at all. Unlike an unreadable database this IS an answer — but not
					 * the answer "it is down": a server that has never finished starting has not
					 * registered yet, and killing it mid-start would make that permanent. The
					 * supervisor's own launch delay is what covers a genuinely failed start. */
					Log.Warning(logSource,
						$"No server is registered under the name '{serverName}' in the {tier} tier. " +
						"Reporting healthy; check that the name matches the server's own ServerName setting.");
					return true;
				}

				double age = (DateTime.UtcNow - lastPulse.Value).TotalSeconds;
				if (age > staleSeconds)
				{
					Log.Warning(logSource,
						$"'{serverName}' last pulsed {age:0}s ago, over the {staleSeconds}s limit. Reporting unhealthy.");
					return false;
				}
				return true;
			}
			catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
			{
				Log.Error(logSource,
					$"Reading the pulse for '{serverName}' timed out. Reporting healthy; a slow database is not a dead server.");
				return true;
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				Log.Error(logSource,
					$"Reading the pulse for '{serverName}' failed: {ex.Message}. Reporting healthy so a database problem " +
					"does not restart every server at once.", ex);
				return true;
			}
		}
	}
}
