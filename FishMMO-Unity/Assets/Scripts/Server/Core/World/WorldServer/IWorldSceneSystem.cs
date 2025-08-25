namespace FishMMO.Server.Core.World.WorldServer
{
	/// <summary>
	/// Engine-agnostic API for managing player connections and scene assignment for a world server.
	/// Implementations provide engine-specific behavior (for example the Unity-backed implementation
	/// in the implementation layer) while keeping signatures free of engine types so core code
	/// can depend on this contract.
	/// </summary>
	public interface IWorldSceneSystem : IServerBehaviour
	{
		/// <summary>
		/// Total number of connections currently managed by the world scene system.
		/// This includes connections that are actively in scenes as well as connections
		/// that are waiting in queues for a scene or instance to become available.
		/// </summary>
		int ConnectionCount { get; }

		/// <summary>
		/// Returns the maximum number of clients allowed in a single instance of the
		/// specified scene. Implementations may consult configuration, caches, or
		/// default limits to determine the value.
		/// </summary>
		/// <param name="sceneName">Identifier or name of the scene.</param>
		/// <returns>Maximum concurrent clients allowed per scene instance (always &gt;= 1).</returns>
		int GetMaxClients(string sceneName);
	}
}