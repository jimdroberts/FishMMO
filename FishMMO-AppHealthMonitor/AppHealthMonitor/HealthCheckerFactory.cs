using FishMMO.Logging;

namespace AppHealthMonitor
{
	/// <summary>
	/// Creates <see cref="IHealthChecker"/> instances for the specified port types.
	/// Returns an empty list when no port types are configured (process-only monitoring).
	/// Logs a warning when UDP is the only configured port type, as it provides no real health signal.
	/// </summary>
	public static class HealthCheckerFactory
	{
		/// <summary>
		/// Singleton health checker instances. Each checker is fully stateless — connections are
		/// created and disposed within each <see cref="IHealthChecker.IsResponsiveAsync"/> call —
		/// so a single instance can be safely shared across all monitors.
		/// </summary>

		/// <summary>Singleton TCP health checker instance.</summary>
		private static readonly IHealthChecker TcpInstance = new TcpHealthChecker();

		/// <summary>Singleton UDP health checker instance.</summary>
		private static readonly IHealthChecker UdpInstance = new UdpHealthChecker();



		/// <summary>
		/// Creates a read-only list of health checkers corresponding to the given port types.
		/// </summary>
		/// <param name="portTypes">The port types to create checkers for. An empty list results in process-only monitoring.</param>
		/// <returns>A read-only list of health checker instances.</returns>
		/// <exception cref="ArgumentNullException">Thrown when <paramref name="portTypes"/> is null.</exception>
		/// <summary>
		/// Builds the checkers for one application.
		/// </summary>
		/// <param name="portTypes">The configured checks.</param>
		/// <param name="config">
		/// The application, for the pulse settings. A <c>DatabasePulse</c> check needs to know
		/// which server to look up, which the check list alone cannot say.
		/// </param>
		/// <param name="board">
		/// The database read service, or null when no control plane is configured. A
		/// <c>DatabasePulse</c> check is skipped with a warning rather than failing the daemon:
		/// a deployment with no database still supervises, and refusing to start would turn a
		/// configuration mismatch into an outage.
		/// </param>
		public static IReadOnlyList<IHealthChecker> Create(
			IReadOnlyList<PortType> portTypes,
			AppConfig config = null,
			FishMMO.Database.Npgsql.Services.Interfaces.IServerBoardService board = null)
		{
			ArgumentNullException.ThrowIfNull(portTypes);

			if (portTypes.Count == 0)
			{
				return Array.Empty<IHealthChecker>();
			}

			var checkers = new List<IHealthChecker>(portTypes.Count);
			bool hasRealSignal = false;

			foreach (var portType in portTypes)
			{
				if (portType == PortType.DatabasePulse)
				{
					if (board == null || config == null)
					{
						Log.Warning("HealthCheckerFactory",
							$"'{config?.Name}' is configured for a DatabasePulse check but no database is available. " +
							"Skipping it; the application is still supervised by its process and any other checks.");
						continue;
					}
					checkers.Add(new PulseHealthChecker(board, config.PulseTier, config.PulseServerName, config.PulseStaleSeconds));
					hasRealSignal = true;
					continue;
				}

				IHealthChecker checker = portType switch
				{
					PortType.TCP => TcpInstance,
					PortType.UDP => UdpInstance,
					_ => throw new ArgumentOutOfRangeException(nameof(portTypes), portType, $"Unsupported check '{portType}'.")
				};

				// UDP is the one check that proves nothing on its own; see PortType.UDP.
				if (portType != PortType.UDP)
				{
					hasRealSignal = true;
				}

				checkers.Add(checker);
			}

			if (!hasRealSignal)
			{
				Log.Warning("HealthCheckerFactory",
					"UDP is the only configured check. A UDP send succeeds whether or not anything is listening, so this " +
					"cannot tell a healthy application from one that is not running. Use DatabasePulse for a FishMMO game " +
					"server, or TCP for anything that listens on TCP.");
			}

			return checkers;
		}
	}
}