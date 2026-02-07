using System.Collections.Generic;
using FishMMO.Database.Data;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime data container for scene instance tracking and scene handle management.
	/// Provides read-only access to scene instance collections.
	/// </summary>
	public interface ISceneInstanceMappingData : IRuntimeDataContainer
	{
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
		Dictionary<long, SceneData> PendingScenes { get; }
	}
}