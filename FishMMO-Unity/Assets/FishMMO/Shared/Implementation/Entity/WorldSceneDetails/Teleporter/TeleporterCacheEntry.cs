using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable entry representing a single teleporter destination in the TeleporterCache.
	/// Stores the stable GUID, scene, display name, position, and rotation for a destination.
	/// </summary>
	[Serializable]
	public class TeleporterCacheEntry
	{
		/// <summary>
		/// The stable unique identifier for this teleporter destination.
		/// </summary>
		public string DestinationID;

		/// <summary>
		/// The name of the scene containing this destination.
		/// </summary>
		public string SceneName;

		/// <summary>
		/// The display name of the destination GameObject, used for editor dropdown labels.
		/// </summary>
		public string DisplayName;

		/// <summary>
		/// The world position of the destination.
		/// </summary>
		public Vector3 Position;

		/// <summary>
		/// The rotation at the destination.
		/// </summary>
		public Quaternion Rotation;
	}
}
