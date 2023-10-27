using UnityEngine;
using System;

namespace FishMMO.Shared
{
	[Serializable]
	public class CharacterInitialSpawnPosition
	{
		public string SpawnerName;
		public string SceneName;
		public Vector3 Position;
		public Quaternion Rotation;
	}
}