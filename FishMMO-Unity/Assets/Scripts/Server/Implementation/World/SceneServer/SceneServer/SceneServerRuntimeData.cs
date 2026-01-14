using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for scene server identification and state.
	/// Manages scene server operational state separately from SceneServerSystem logic.
	/// </summary>
	public class SceneServerRuntimeData : RuntimeDataContainer, ISceneServerRuntimeData
	{
		/// <summary>
		/// Database ID for this scene server instance.
		/// </summary>
		public long ID { get; set; }

		/// <summary>
		/// Indicates whether the scene server is locked (not accepting new connections).
		/// </summary>
		public bool IsLocked { get; set; }

		/// <summary>
		/// Initializes the scene server runtime data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all scene server runtime data.
		/// </summary>
		public override void Clear()
		{
			ID = 0;
			IsLocked = false;
		}

		/// <summary>
		/// Deinitializes the scene server runtime data container.
		/// </summary>
		public override void Deinitialize()
		{
			Clear();
		}
	}
}
