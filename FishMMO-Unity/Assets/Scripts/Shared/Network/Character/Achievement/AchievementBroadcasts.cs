using System.Collections.Generic;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct AchievementUpdateBroadcast : IBroadcast
	{
		public int templateID;
		public uint newValue;
	}

	public struct AchievementUpdateMultipleBroadcast : IBroadcast
	{
		public List<AchievementUpdateBroadcast> achievements;
	}
}