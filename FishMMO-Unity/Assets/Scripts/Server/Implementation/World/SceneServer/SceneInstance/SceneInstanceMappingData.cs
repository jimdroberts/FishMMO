using System.Collections.Generic;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for scene instance tracking and scene handle management.
	/// Manages all scene instance state separately from SceneServerSystem logic.
	/// </summary>
	public class SceneInstanceMappingData : RuntimeDataContainer, ISceneInstanceMappingData
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
		/// Maps world server IDs to scene names and handles, tracking all loaded scene instances.
		/// </summary>
		public Dictionary<long, Dictionary<string, Dictionary<int, ISceneInstanceDetails>>> WorldScenes { get; private set; }

		/// <summary>
		/// Maps scene handles to scene names for quick lookup.
		/// </summary>
		public Dictionary<int, string> SceneNameByHandle { get; private set; }

		/// <summary>
		/// Tracks pending scene load requests by scene ID.
		/// </summary>
		public Dictionary<long, SceneEntity> PendingScenes { get; private set; }

		/// <summary>
		/// Initializes the scene instance mapping data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			WorldScenes = new Dictionary<long, Dictionary<string, Dictionary<int, ISceneInstanceDetails>>>();
			SceneNameByHandle = new Dictionary<int, string>();
			PendingScenes = new Dictionary<long, SceneEntity>();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all scene instance mapping data.
		/// </summary>
		public override void Clear()
		{
			WorldScenes?.Clear();
			SceneNameByHandle?.Clear();
			PendingScenes?.Clear();
		}

		/// <summary>
		/// Deinitializes the scene instance mapping data container.
		/// </summary>
		public override void Deinitialize()
		{
			Clear();
		}
	}
}