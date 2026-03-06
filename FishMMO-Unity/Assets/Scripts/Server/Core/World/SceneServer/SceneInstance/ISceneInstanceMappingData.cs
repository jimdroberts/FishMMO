using System.Collections.Generic;

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
		/// Flat O(1) lookup from scene handle to instance details.
		/// Kept in sync with the nested WorldScenes dictionary.
		/// </summary>
		Dictionary<int, ISceneInstanceDetails> SceneInstanceByHandle { get; }

		/// <summary>
		/// Tracks pending scene load requests by scene ID.
		/// Each entry combines the SceneData with its enqueue timestamp
		/// for TTL expiration, eliminating dual-map sync risk.
		/// </summary>
		Dictionary<long, PendingSceneInfo> PendingScenes { get; }
	}
}