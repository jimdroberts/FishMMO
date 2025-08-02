using UnityEngine;
using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable class containing details for a character's initial spawn position, including location, rotation, and allowed races.
	/// </summary>
	[Serializable]
	public class CharacterInitialSpawnPositionDetails
	{
		/// <summary>
		/// The name of the spawner associated with this initial spawn position.
		/// </summary>
		public string SpawnerName;

		/// <summary>
		/// The name of the scene where the character will spawn.
		/// </summary>
		public string SceneName;

		/// <summary>
		/// The world position where the character will initially spawn.
		/// </summary>
		public Vector3 Position;

		/// <summary>
		/// The rotation to apply to the character at the initial spawn position.
		/// </summary>
		public Quaternion Rotation;

		/// <summary>
		/// List of races allowed to spawn at this position.
		/// </summary>
		public List<RaceTemplate> AllowedRaces;
	}
}