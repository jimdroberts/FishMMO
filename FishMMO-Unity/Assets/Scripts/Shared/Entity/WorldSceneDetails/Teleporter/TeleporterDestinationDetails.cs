using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable data structure representing the destination details for a teleporter.
	/// Includes the target scene name, position, and rotation for the destination point.
	/// </summary>
	[Serializable]
	public class TeleporterDestinationDetails
	{
		/// <summary>
		/// The name of the target scene to teleport to.
		/// </summary>
		public string Scene;

		/// <summary>
		/// The world position within the target scene where the teleported entity will appear.
		/// </summary>
		public Vector3 Position;

		/// <summary>
		/// The rotation to apply to the teleported entity at the destination position.
		/// </summary>
		public Quaternion Rotation;
	}
}