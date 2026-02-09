namespace AppHealthMonitor
{
	/// <summary>
	/// Defines a contract for checking the health of a specific port type.
	/// Implementations should handle their own timeout and cleanup logic.
	/// </summary>
	public interface IHealthChecker
	{
		/// <summary>
		/// Gets the port type this health checker handles.
		/// </summary>
		PortType PortType { get; }

		/// <summary>
		/// Checks if the specified port is responsive.
		/// </summary>
		/// <param name="host">The hostname or IP address to check.</param>
		/// <param name="port">The port number to check.</param>
		/// <param name="timeoutMilliseconds">The timeout in milliseconds before the check is considered failed.</param>
		/// <param name="cancellationToken">Token to signal cancellation of the check.</param>
		/// <returns>True if the port is responsive; otherwise, false.</returns>
		Task<bool> IsResponsiveAsync(string host, int port, int timeoutMilliseconds, CancellationToken cancellationToken);
	}
}