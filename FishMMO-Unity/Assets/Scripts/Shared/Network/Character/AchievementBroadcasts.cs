using System.Collections.Generic;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct AchievementUpdateBroadcast : IBroadcast
	{
		public int TemplateID;
		public uint Value;
		public byte Tier;
	}

	public struct AchievementUpdateMultipleBroadcast : IBroadcast
	{
		public List<AchievementUpdateBroadcast> Achievements;
	}
}