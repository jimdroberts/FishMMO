using System;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.SceneServer
{
	/// <summary>
	/// Stores details about a scene instance managed by the server, including server IDs, name, handle, type, and character count.
	/// Tracks when the last character exited for stale scene detection.
	/// </summary>
	public class SceneInstanceDetails : ISceneInstanceDetails
	{
		/// <summary>
		/// The world server ID associated with this scene instance.
		/// </summary>
		public long WorldServerID { get; set; }
		/// <summary>
		/// The scene server ID associated with this scene instance.
		/// </summary>
		public long SceneServerID { get; set; }
		/// <summary>
		/// The name of the scene instance.
		/// </summary>
		public string Name { get; set; }
		/// <summary>
		/// The handle (unique identifier) for this scene instance.
		/// </summary>
		public int Handle { get; set; }
		/// <summary>
		/// The type of scene (OpenWorld, Group, PvP, etc.).
		/// </summary>
		public SceneType SceneType { get; set; }
		/// <summary>
		/// The current number of characters in this scene instance.
		/// </summary>
		public int CharacterCount { get; set; }
		/// <summary>
		/// Indicates whether the scene is stale (no characters present).
		/// </summary>
		public bool StalePulse { get { return CharacterCount < 1; } }
		/// <summary>
		/// The time when the last character exited the scene.
		/// </summary>
		public DateTime LastExit { get; set; }

		/// <summary>
		/// Adds or subtracts from the character count, updating LastExit if the scene becomes empty.
		/// </summary>
		/// <param name="count">Amount to add or subtract from the character count.</param>
		public void AddCharacterCount(int count)
		{
			//UnityEngine.Log.Debug($"{Name} adding {count} to CharacterCount {CharacterCount}");
			CharacterCount += count;
			if (CharacterCount < 1)
			{
				LastExit = DateTime.UtcNow;
			}
		}
	}
}