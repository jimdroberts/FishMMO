using FishMMO.Server.Core;
using FishMMO.Server.Core.World.WorldServer;

namespace FishMMO.Server.Implementation.World.WorldServer
{
	/// <summary>
	/// Runtime data container for world server instance state.
	/// Manages world server ID and lock status separately from WorldServerSystem logic.
	/// </summary>
	public class WorldServerSystemRuntimeData : RuntimeDataContainer, IWorldServerSystemRuntimeData
	{
		/// <summary>
		/// Database ID for this world server instance.
		/// </summary>
		public long ID { get; set; }

		/// <summary>
		/// Indicates whether the world server is locked (not accepting new connections).
		/// </summary>
		public bool IsLocked { get; set; }

		/// <summary>
		/// Initializes the world server runtime data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears the world server state.
		/// </summary>
		public override void Clear()
		{
			ID = 0;
			IsLocked = false;
		}

		/// <summary>
		/// Deinitializes the world server runtime data container.
		/// </summary>
		public override void Deinitialize()
		{
			Clear();
		}
	}
}