using UnityEngine;
using System;

namespace FishMMO.Shared
{
	[Serializable]
	public class AchievementTier
	{
		public uint MaxValue;
		public string TierCompleteMessage;
		public AudioClip CompleteSound;
		public BaseItemTemplate[] ItemRewards;
		public BaseBuffTemplate[] BuffRewards;
		public string[] TitleRewards;
		public AbilityTemplate[] AbilityRewards;
	}
}