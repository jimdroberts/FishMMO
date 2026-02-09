namespace AppHealthMonitor
{
	/// <summary>
	/// Specifies the type of port monitoring to perform for an application.
	/// An empty list of <see cref="PortType"/> indicates process-only monitoring.
	/// </summary>
	public enum PortType
	{
		/// <summary>
		/// TCP port monitoring. Checks if TCP connections can be established.
		/// </summary>
		TCP,

		/// <summary>
		/// UDP port monitoring. Checks if UDP datagrams can be sent.
		/// </summary>
		UDP,

		/// <summary>
		/// WebSocket port monitoring. Checks if WebSocket connections can be established.
		/// </summary>
		WebSocket
	}
}