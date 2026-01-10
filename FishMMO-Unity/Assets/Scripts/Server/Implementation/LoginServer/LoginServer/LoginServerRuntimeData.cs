using FishMMO.Server.Core;
using FishMMO.Server.Core.LoginServer;

namespace FishMMO.Server.Implementation.LoginServer
{
	/// <summary>
	/// Runtime data container for login server state, storing the unique server ID.
	/// </summary>
	public class LoginServerRuntimeData : RuntimeDataContainer, ILoginServerRuntimeData
	{
		/// <summary>
		/// Gets the unique ID of this login server instance.
		/// </summary>
		public long ID { get; set; }

		/// <summary>
		/// Initializes the runtime data once. Called when the data container is first set up.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			ID = 0;
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears the runtime data. Called when resetting state between sessions.
		/// </summary>
		public override void Clear()
		{
			ID = 0;
		}

		/// <summary>
		/// Deinitializes the runtime data. Called when shutting down the server.
		/// </summary>
		public override void Deinitialize()
		{
			ID = 0;
		}
	}
}