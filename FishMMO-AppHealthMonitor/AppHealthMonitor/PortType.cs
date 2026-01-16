namespace AppHealthMonitor
{
	/// <summary>
	/// Specifies the type of port monitoring to perform for an application.
	/// </summary>
	public enum PortType
	{
		/// <summary>
		/// No port monitoring. Only process health is checked.
		/// </summary>
		None,

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