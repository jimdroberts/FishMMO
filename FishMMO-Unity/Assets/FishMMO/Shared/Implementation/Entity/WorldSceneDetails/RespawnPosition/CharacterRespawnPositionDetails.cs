using UnityEngine;
using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable class containing position and rotation details for a character's respawn location.
	/// </summary>
	[Serializable]
	public class CharacterRespawnPositionDetails
	{
		/// <summary>
		/// The world position where the character will respawn.
		/// </summary>
		public Vector3 Position;

		/// <summary>
		/// The rotation to apply to the character at the respawn position.
		/// </summary>
		public Quaternion Rotation;
	}
}