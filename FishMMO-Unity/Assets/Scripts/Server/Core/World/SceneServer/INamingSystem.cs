using FishMMO.Shared;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Engine-agnostic public API for the naming system used to resolve IDs to
	/// human-readable names and to resolve names back to IDs. Implementations
	/// should prefer local caches when available and fall back to database lookups.
	/// The connection parameter is typed as <c>object</c> so this contract remains
	/// independent of the networking transport.
	/// </summary>
	public interface INamingSystem<TConnection> : IServerBehaviour
	{
		/// <summary>
		/// Send a resolved name for the given ID back to the requesting connection.
		/// </summary>
		/// <param name="conn">Opaque connection object representing the requester.</param>
		/// <param name="type">Type of naming resolution (for example character or guild).</param>
		/// <param name="id">Identifier that was resolved.</param>
		/// <param name="name">Resolved human-readable name for the id.</param>
		void SendNamingBroadcast(TConnection conn, NamingSystemType type, long id, string name);

		/// <summary>
		/// Send the result of a reverse-name lookup (name -> id) back to the requester.
		/// If the name was not found the implementation should send an appropriate
		/// payload indicating failure (commonly id = 0 and empty name).
		/// </summary>
		/// <param name="conn">Opaque connection object representing the requester.</param>
		/// <param name="type">Type of naming resolution requested.</param>
		/// <param name="nameLowerCase">Lowercased name that was searched for.</param>
		/// <param name="id">Resolved identifier (0 if not found).</param>
		/// <param name="name">Resolved canonical name (empty if not found).</param>
		void SendReverseNamingBroadcast(TConnection conn, NamingSystemType type, string nameLowerCase, long id, string name);
	}
}