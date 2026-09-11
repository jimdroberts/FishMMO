namespace AppHealthMonitor
{
	/// <summary>
	/// The health checks an application can be configured with.
	/// </summary>
	/// <remarks>
	/// The name has outgrown the type: one of these is not a port at all. Renaming it is
	/// config-compatible — the JSON carries the member names, not the type name — and is worth
	/// doing when something else brings someone into this file.
	/// </remarks>
	public enum PortType
	{
		/// <summary>Open a TCP connection. Real evidence, for anything that listens on TCP.</summary>
		TCP,

		/// <summary>
		/// Send a UDP datagram.
		/// </summary>
		/// <remarks>
		/// <b>This proves almost nothing and must not be used alone.</b> UDP is connectionless,
		/// so the send succeeds whether or not anything is listening; the checker returns true
		/// as soon as the local stack accepts the datagram and never waits for a reply. It
		/// cannot tell a healthy server from one that is not running.
		/// </remarks>
		UDP,

		/// <summary>
		/// Read the server's own pulse from the database.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The right check for a FishMMO game server. The transport is WebTransport over QUIC,
		/// which is UDP-only, so there is no TCP listener to connect to — a TCP check against a
		/// game port fails against a perfectly healthy server, and the supervisor then restarts
		/// it until it gives up. And QUIC has no "is the port open" answer to replace it with:
		/// the server responds only to a valid Initial packet carrying a TLS ClientHello, so a
		/// genuine probe means performing a handshake.
		/// </para>
		/// <para>
		/// The pulse sidesteps all of that and proves more: the server is running its loop,
		/// doing work, and can still reach the database. A socket probe only ever proves a
		/// socket is bound.
		/// </para>
		/// </remarks>
		DatabasePulse,
	}
}
