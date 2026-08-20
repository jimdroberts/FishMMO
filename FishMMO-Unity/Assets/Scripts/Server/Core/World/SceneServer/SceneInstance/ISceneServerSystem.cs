using FishMMO.Shared;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Engine-agnostic public API for a Scene Server system responsible for
	/// loading and unloading scenes, tracking scene instances, and sending
	/// periodic heartbeats (pulses) to connected world servers.
	/// </summary>
	/// <remarks>
	/// Implementations must avoid exposing engine-specific types on the public
	/// surface. Methods use plain object references for connection handles so
	/// different engine/networking implementations can be supported.
	/// Thread-safety and lifetime semantics (for example when unloading during
	/// shutdown) are implementation details but should be documented by concrete
	/// implementations.
	/// </remarks>
	public interface ISceneServerSystem<TConnection> : IServerBehaviour
	{
		/// <summary>
		/// Pulse/heartbeat rate, in seconds. Controls how frequently the system
		/// sends status updates to world servers.
		/// </summary>
		float PulseRate { get; }

		/// <summary>
		/// Gets the world scene details cache for fast lookups of scene instance info.
		/// </summary>
		WorldSceneDetailsCache WorldSceneDetailsCache { get; }

		/// <summary>
		/// Attempts to retrieve instance details for the specified scene instance.
		/// </summary>
		/// <param name="worldServerID">The id of the world server that owns the scene.</param>
		/// <param name="sceneName">The canonical name of the scene.</param>
		/// <param name="sceneID">The scene row ID identifying the instance. See <see cref="ISceneInstanceDetails.SceneID"/>.</param>
		/// <param name="instanceDetails">When this method returns <c>true</c>, contains the instance details.</param>
		/// <returns><c>true</c> if the instance was found; otherwise <c>false</c>.</returns>
		bool TryGetSceneInstanceDetails(long worldServerID, string sceneName, long sceneID, out ISceneInstanceDetails instanceDetails);

		/// <summary>
		/// Ensures the specified scene instance is loaded and associates the
		/// provided connection with that instance.
		/// </summary>
		/// <param name="connection">An engine-agnostic connection object representing the client/peer.</param>
		/// <param name="instance">The instance details describing which scene to load/join.</param>
		/// <returns><c>true</c> when the scene was successfully loaded/associated; otherwise <c>false</c>.</returns>
		bool TryLoadSceneForConnection(TConnection connection, ISceneInstanceDetails instance);

		/// <summary>
		/// Removes the given connection from the named scene. If the connection
		/// is the last participant the implementation may choose to unload the
		/// scene instance.
		/// </summary>
		/// <param name="connection">The connection to remove.</param>
		/// <param name="sceneName">The canonical scene name from which to remove the connection.</param>
		void UnloadSceneForConnection(TConnection connection, string sceneName);

		/// <summary>
		/// Unloads the scene with the given handle, releasing any associated
		/// resources and notifying world servers as appropriate. If the handle is
		/// unknown this method should be a no-op.
		/// </summary>
		/// <param name="sceneID">The scene row ID of the instance to unload.</param>
		void UnloadScene(long sceneID);

		/// <summary>
		/// Adjusts the tracked character count for a scene instance.
		/// </summary>
		/// <remarks>
		/// Exposed so the character system can keep a scene populated while it holds a
		/// combat-logout body. Those bodies are removed from the connection maps the count is
		/// otherwise derived from, so without this a scene containing only unattended bodies
		/// reports itself empty — it becomes a stale-pulse candidate for unload, and the world
		/// server sees capacity that is not really there.
		/// </remarks>
		/// <param name="worldServerID">World server that owns the scene instance.</param>
		/// <param name="sceneName">Scene name.</param>
		/// <param name="sceneID">Scene row ID identifying the instance.</param>
		/// <param name="amount">Amount to adjust by.</param>
		void AdjustSceneCharacterCount(long worldServerID, string sceneName, long sceneID, int amount);
	}
}