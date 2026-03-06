using System.Collections.Generic;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for scene instance tracking and scene handle management.
	/// Manages all scene instance mappings separately from SceneServerSystem logic.
	/// </summary>
	public class SceneInstanceMappingData : RuntimeDataContainer, ISceneInstanceMappingData
	{
		/// <summary>
		/// Maps world server IDs to scene names and handles, tracking all loaded scene instances.
		/// </summary>
		public Dictionary<long, Dictionary<string, Dictionary<int, ISceneInstanceDetails>>> WorldScenes { get; private set; }

		/// <summary>
		/// Maps scene handles to scene names for quick lookup.
		/// </summary>
		public Dictionary<int, string> SceneNameByHandle { get; private set; }

		/// <summary>
		/// Flat O(1) lookup from scene handle to instance details.
		/// Kept in sync with the nested WorldScenes dictionary.
		/// </summary>
		public Dictionary<int, ISceneInstanceDetails> SceneInstanceByHandle { get; private set; }

		/// <summary>
		/// Tracks pending scene load requests by scene ID.
		/// Each entry combines the SceneData with its enqueue timestamp.
		/// </summary>
		public Dictionary<long, PendingSceneInfo> PendingScenes { get; private set; }

		/// <summary>
		/// Initializes the scene instance mapping data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			WorldScenes = new Dictionary<long, Dictionary<string, Dictionary<int, ISceneInstanceDetails>>>();
			SceneNameByHandle = new Dictionary<int, string>();
			SceneInstanceByHandle = new Dictionary<int, ISceneInstanceDetails>();
			PendingScenes = new Dictionary<long, PendingSceneInfo>();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all scene instance mapping data.
		/// </summary>
		public override void Clear()
		{
			WorldScenes?.Clear();
			SceneNameByHandle?.Clear();
			SceneInstanceByHandle?.Clear();
			PendingScenes?.Clear();
		}

		/// <summary>
		/// Deinitializes the scene instance mapping data container.
		/// </summary>
		protected override void OnDeinitialize()
		{
			Clear();
		}
	}
}