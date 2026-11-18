using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable entry representing a single scene teleporter in the TeleporterCache.
	/// Stores the teleporter name, scene, destination connection, and position.
	/// </summary>
	[Serializable]
	public class SceneTeleporterCacheEntry
	{
		/// <summary>
		/// The name of the teleporter GameObject, used as the runtime lookup key within a scene.
		/// </summary>
		public string TeleporterName;

		/// <summary>
		/// The name of the scene containing this teleporter.
		/// </summary>
		public string SceneName;

		/// <summary>
		/// The asset path to the scene containing this teleporter.
		/// Used by editor tooling to open the source scene directly.
		/// </summary>
		public string ScenePath;

		/// <summary>
		/// The DestinationID this teleporter connects to, referencing a TeleporterCacheEntry.
		/// </summary>
		public string DestinationID;

		/// <summary>
		/// Persistent GlobalObjectId string for this teleporter object in the scene.
		/// Used by editor tooling to re-select the exact object after scene load.
		/// </summary>
		public string TeleporterGlobalObjectId;

		/// <summary>
		/// The world position of the teleporter.
		/// </summary>
		public Vector3 Position;
	}
}
