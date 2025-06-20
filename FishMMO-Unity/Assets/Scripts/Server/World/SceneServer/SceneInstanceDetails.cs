using System;

namespace FishMMO.Server
{
	public class SceneInstanceDetails
	{
		public long WorldServerID;
		public long SceneServerID;
		public string Name;
		public int Handle;
		public SceneType SceneType;
		public int CharacterCount;
		public bool StalePulse { get { return CharacterCount < 1; } }
		public DateTime LastExit = DateTime.UtcNow;

		public void AddCharacterCount(int count)
		{
			//UnityEngine.Debug.Log($"{Name} adding {count} to CharacterCount {CharacterCount}");
			CharacterCount += count;
			if (CharacterCount < 1)
			{
				LastExit = DateTime.UtcNow;
			}
		}
	}
}