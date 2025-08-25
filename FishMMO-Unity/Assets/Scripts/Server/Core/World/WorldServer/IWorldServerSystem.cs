namespace FishMMO.Server.Core.World.WorldServer
{
	/// <summary>
	/// Core-facing abstraction representing a world server process that manages
	/// scenes and player population for an MMO world. The implementation layer
	/// provides concrete behavior (for example <c>WorldServerSystem</c>).
	/// </summary>
	public interface IWorldServerSystem : IServerBehaviour
	{
		/// <summary>
		/// Persistent database identifier for this world server instance. Used when
		/// recording scenes, heartbeats, and ownership in the central database.
		/// </summary>
		long ID { get; }

		/// <summary>
		/// True when the server is locked and should not accept new player connections.
		/// Implementations may set this when undergoing maintenance or when the server
		/// has reached capacity.
		/// </summary>
		bool IsLocked { get; }

		/// <summary>
		/// Registers or updates the server record in the central database. This call
		/// should create the record the first time the server comes online and update
		/// connection info on subsequent calls.
		/// </summary>
		/// <param name="serverAddress">Public address or hostname of the server.</param>
		/// <param name="port">Port number where scene servers accept connections.</param>
		/// <param name="characterCount">Current number of connected characters to record.</param>
		void Register(string serverAddress, ushort port, int characterCount);

		/// <summary>
		/// Sends a periodic heartbeat (pulse) to the database or monitoring systems
		/// with the current character count. Implementations should use this to keep
		/// the server liveness and population metrics up-to-date.
		/// </summary>
		/// <param name="characterCount">Current number of connected characters.</param>
		void Pulse(int characterCount);
	}
}