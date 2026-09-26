using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// What a queue row is waiting for: one kind of instance, one scene, one difficulty or format.
	/// </summary>
	/// <remarks>
	/// The unit the group finder counts, forms and backfills by. Rows that share a key are
	/// interchangeable to the matcher; rows that do not are never placed together.
	/// </remarks>
	public readonly struct GroupFinderQueueKey : IEquatable<GroupFinderQueueKey>
	{
		/// <summary>Instance kind: the shared <c>SceneType</c> value (2 dungeon, 3 arena).</summary>
		public readonly int SceneType;
		/// <summary>Dungeon or arena scene.</summary>
		public readonly string SceneName;
		/// <summary>Difficulty index for a dungeon; format index for an arena.</summary>
		public readonly int Difficulty;

		public GroupFinderQueueKey(int sceneType, string sceneName, int difficulty)
		{
			SceneType = sceneType;
			SceneName = sceneName ?? string.Empty;
			Difficulty = difficulty;
		}

		public bool Equals(GroupFinderQueueKey other)
		{
			return SceneType == other.SceneType &&
				Difficulty == other.Difficulty &&
				string.Equals(SceneName, other.SceneName, StringComparison.Ordinal);
		}

		public override bool Equals(object? obj)
		{
			return obj is GroupFinderQueueKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(SceneType, SceneName != null ? StringComparer.Ordinal.GetHashCode(SceneName) : 0, Difficulty);
		}

		public override string ToString()
		{
			return $"{SceneType}:{SceneName}:{Difficulty}";
		}
	}
}
