using FishMMO.Logging;

namespace AppHealthMonitor
{
	/// <summary>
	/// Creates <see cref="IHealthChecker"/> instances for the specified port types.
	/// Returns an empty list when no port types are configured (process-only monitoring).
	/// </summary>
	public static class HealthCheckerFactory
	{
		/// <summary>
		/// Creates a read-only list of health checkers corresponding to the given port types.
		/// </summary>
		/// <param name="portTypes">The port types to create checkers for. An empty list results in process-only monitoring.</param>
		/// <returns>A read-only list of health checker instances.</returns>
		public static IReadOnlyList<IHealthChecker> Create(IReadOnlyList<PortType> portTypes)
		{
			if (portTypes.Count == 0)
			{
				return Array.Empty<IHealthChecker>();
			}

			var checkers = new List<IHealthChecker>(portTypes.Count);

			foreach (var portType in portTypes)
			{
				switch (portType)
				{
					case PortType.TCP:
						checkers.Add(new TcpHealthChecker());
						break;
					case PortType.UDP:
						checkers.Add(new UdpHealthChecker());
						break;
					case PortType.WebSocket:
						checkers.Add(new WebSocketHealthChecker());
						break;
					default:
						Log.Warning("HealthCheckerFactory", $"Unsupported PortType '{portType}'. No health checker created.");
						break;
				}
			}

			return checkers;
		}
	}
}