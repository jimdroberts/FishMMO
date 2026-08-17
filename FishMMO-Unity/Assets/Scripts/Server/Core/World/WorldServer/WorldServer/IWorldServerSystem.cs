using System.Threading;
using System.Threading.Tasks;

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
		/// Registers or updates the server record in the central database. This call
		/// should create the record the first time the server comes online and update
		/// connection info on subsequent calls.
		/// </summary>
		/// <param name="serverAddress">Public address or hostname of the server.</param>
		/// <param name="port">Port number where scene servers accept connections.</param>
		/// <param name="characterCount">Current number of connected characters to record.</param>
		/// <param name="cancellationToken">Cancelled when the server shuts down mid-registration.</param>
		/// <returns><c>true</c> when the server record was written.</returns>
		/// <remarks>
		/// Asynchronous by contract: registration is database I/O, and the Unity main thread must
		/// never block on it. The main thread is what drains async continuations, so blocking it
		/// stalls the very work being waited on.
		/// </remarks>
		Task<bool> RegisterAsync(string serverAddress, ushort port, int characterCount, CancellationToken cancellationToken = default);

		/// <summary>
		/// Sends a periodic heartbeat (pulse) to the database or monitoring systems
		/// with the current character count. Implementations should use this to keep
		/// the server liveness and population metrics up-to-date.
		/// </summary>
		/// <param name="characterCount">Current number of connected characters.</param>
		void Pulse(int characterCount);
	}
}