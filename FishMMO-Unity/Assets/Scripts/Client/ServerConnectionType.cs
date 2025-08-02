namespace FishMMO.Client
{
	/// <summary>
	/// Represents the type of server connection the client is currently using.
	/// Used to track connection state transitions in the client.
	/// </summary>
	public enum ServerConnectionType : byte
	{
		/// <summary>
		/// No active server connection.
		/// </summary>
		None,
		/// <summary>
		/// Connected to the login server.
		/// </summary>
		Login,
		/// <summary>
		/// Connecting to the world server (transition state).
		/// </summary>
		ConnectingToWorld,
		/// <summary>
		/// Connected to the world server.
		/// </summary>
		World,
		/// <summary>
		/// Connected to a specific scene server.
		/// </summary>
		Scene,
	}
}