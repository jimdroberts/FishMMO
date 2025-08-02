using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable class containing details for a scene teleporter destination, including target scene, position, and rotation.
	/// </summary>
	[Serializable]
	public class SceneTeleporterDetails
	{
		/// <summary>
		/// The source teleporter name (internal use).
		/// </summary>
		internal string From;

		/// <summary>
		/// The name of the scene to teleport to.
		/// </summary>
		public string ToScene;

		/// <summary>
		/// The position in the target scene where the character will be teleported.
		/// </summary>
		public Vector3 ToPosition;

		/// <summary>
		/// The rotation to apply to the character at the teleport destination.
		/// </summary>
		public Quaternion ToRotation;
	}
}