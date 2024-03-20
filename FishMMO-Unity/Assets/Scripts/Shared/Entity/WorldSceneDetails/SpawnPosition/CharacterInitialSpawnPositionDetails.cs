using UnityEngine;
using System;

namespace FishMMO.Shared
{
	[Serializable]
	public class CharacterInitialSpawnPositionDetails
	{
		public string SpawnerName;
		public string SceneName;
		public Vector3 Position;
		public Quaternion Rotation;
	}
}