using UnityEngine;
using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	[Serializable]
	public class AchievementTier
	{
		public uint MaxValue;
		public string TierCompleteMessage;
		public AudioClip CompleteSound;
		public List<BaseAbilityTemplate> AbilityRewards;
		public List<BaseItemTemplate> ItemRewards;
		public List<BaseBuffTemplate> BuffRewards;
		public List<string> TitleRewards;
	}
}