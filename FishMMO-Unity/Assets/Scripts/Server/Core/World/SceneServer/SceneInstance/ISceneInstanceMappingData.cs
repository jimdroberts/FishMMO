using System.Collections.Generic;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime data container for scene instance tracking and scene handle management.
	/// Provides read-only access to scene instance collections.
	/// </summary>
	public interface ISceneInstanceMappingData : IRuntimeDataContainer
	{
		/// <summary>
		/// Database ID for this scene server instance.
		/// </summary>
		long ID { get; set; }

		/// <summary>
		/// Indicates whether the scene server is locked (not accepting new connections).
		/// </summary>
		bool IsLocked { get; set; }

		/// <summary>
		/// Maps world server IDs to scene names and handles, tracking all loaded scene instances.
		/// </summary>
		Dictionary<long, Dictionary<string, Dictionary<int, ISceneInstanceDetails>>> WorldScenes { get; }

		/// <summary>
		/// Maps scene handles to scene names for quick lookup.
		/// </summary>
		Dictionary<int, string> SceneNameByHandle { get; }

		/// <summary>
		/// Tracks pending scene load requests by scene ID.
		/// </summary>
		Dictionary<long, SceneEntity> PendingScenes { get; }
	}
}