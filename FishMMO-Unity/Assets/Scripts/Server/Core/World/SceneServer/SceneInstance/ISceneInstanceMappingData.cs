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
		/// Maps world server IDs to scene names to scene row IDs, tracking all loaded scene instances.
		/// </summary>
		Dictionary<long, Dictionary<string, Dictionary<long, ISceneInstanceDetails>>> WorldScenes { get; }

		/// <summary>
		/// Maps local scene handles to scene names for quick lookup.
		/// </summary>
		Dictionary<int, string> SceneNameByHandle { get; }

		/// <summary>
		/// Flat O(1) lookup from the local scene manager handle to instance details.
		/// </summary>
		/// <remarks>
		/// Keyed by the process-local handle because that is what scene-unload callbacks report.
		/// Everything that identifies an instance across processes uses
		/// <see cref="SceneInstanceByID"/> instead — see <see cref="ISceneInstanceDetails.SceneID"/>.
		/// </remarks>
		Dictionary<int, ISceneInstanceDetails> SceneInstanceByHandle { get; }

		/// <summary>
		/// Flat O(1) lookup from scene row ID to instance details.
		/// Kept in sync with the nested WorldScenes dictionary and with <see cref="SceneInstanceByHandle"/>.
		/// </summary>
		Dictionary<long, ISceneInstanceDetails> SceneInstanceByID { get; }

		/// <summary>
		/// Tracks pending scene load requests by scene ID.
		/// Each entry combines the SceneData with its enqueue timestamp
		/// for TTL expiration, eliminating dual-map sync risk.
		/// </summary>
		Dictionary<long, PendingSceneInfo> PendingScenes { get; }
	}
}