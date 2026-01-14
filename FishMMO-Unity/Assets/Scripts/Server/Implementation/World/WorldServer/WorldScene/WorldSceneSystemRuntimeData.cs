using FishMMO.Server.Core;
using FishMMO.Server.Core.World.WorldServer;

namespace FishMMO.Server.Implementation.World.WorldServer
{
	/// <summary>
	/// Runtime data container for WorldSceneSystem state.
	/// Stores mutable state separate from the system logic.
	/// </summary>
	public class WorldSceneSystemRuntimeData : RuntimeDataContainer, IWorldSceneSystemRuntimeData
	{
		/// <summary>
		/// Reference to the world server authenticator for login/authentication events.
		/// </summary>
		public WorldServerAuthenticator LoginAuthenticator { get; set; }

		/// <summary>
		/// Time remaining until the next wait queue update.
		/// </summary>
		public float NextWaitQueueUpdate { get; set; }

		/// <summary>
		/// Initializes the runtime data once. Called when the data container is first set up.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			LoginAuthenticator = null;
			NextWaitQueueUpdate = 0.0f;
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears the runtime data. Called when resetting state between sessions.
		/// </summary>
		public override void Clear()
		{
			LoginAuthenticator = null;
			NextWaitQueueUpdate = 0.0f;
		}

		/// <summary>
		/// Deinitializes the runtime data. Called when shutting down the server.
		/// </summary>
		public override void Deinitialize()
		{
			LoginAuthenticator = null;
			NextWaitQueueUpdate = 0.0f;
		}
	}
}
