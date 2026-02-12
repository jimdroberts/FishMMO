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
			bool hasNonUdp = false;

			foreach (var portType in portTypes)
			{
				IHealthChecker checker = portType switch
				{
					PortType.TCP => new TcpHealthChecker(),
					PortType.UDP => new UdpHealthChecker(),
					PortType.WebSocket => new WebSocketHealthChecker(),
					_ => throw new ArgumentOutOfRangeException(nameof(portTypes), portType, $"Unsupported PortType '{portType}'.")
				};

				if (portType != PortType.UDP)
				{
					hasNonUdp = true;
				}

				checkers.Add(checker);
			}

			// Warn if UDP is the only port type — it provides no real health signal.
			if (!hasNonUdp)
			{
				Log.Warning("HealthCheckerFactory", "UDP is the only configured port type. UDP checks cannot verify the remote application is alive. Consider adding TCP or WebSocket for reliable health monitoring.");
			}

			return checkers;
		}
	}
}