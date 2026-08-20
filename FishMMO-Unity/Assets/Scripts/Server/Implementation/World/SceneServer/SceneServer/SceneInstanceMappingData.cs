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
		/// <inheritdoc />
		public Dictionary<long, Dictionary<string, Dictionary<long, ISceneInstanceDetails>>> WorldScenes { get; private set; }

		/// <summary>
		/// Maps scene handles to scene names for quick lookup.
		/// </summary>
		public Dictionary<int, string> SceneNameByHandle { get; private set; }

		/// <inheritdoc />
		public Dictionary<int, ISceneInstanceDetails> SceneInstanceByHandle { get; private set; }

		/// <inheritdoc />
		public Dictionary<long, ISceneInstanceDetails> SceneInstanceByID { get; private set; }

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
			WorldScenes = new Dictionary<long, Dictionary<string, Dictionary<long, ISceneInstanceDetails>>>();
			SceneNameByHandle = new Dictionary<int, string>();
			SceneInstanceByHandle = new Dictionary<int, ISceneInstanceDetails>();
			SceneInstanceByID = new Dictionary<long, ISceneInstanceDetails>();
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
			SceneInstanceByID?.Clear();
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