using UnityEngine;
using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a single tier or milestone within an achievement, including rewards and completion data.
	/// </summary>
	[Serializable]
	public class AchievementTier
	{
		/// <summary>
		/// The value required to complete this tier (e.g., number of kills, points, etc).
		/// </summary>
		public uint Value;

		/// <summary>
		/// The message shown to the player when this tier is completed.
		/// </summary>
		public string TierCompleteMessage;

		/// <summary>
		/// The sound played when this tier is completed.
		/// </summary>
		public AudioClip CompleteSound;

		/// <summary>
		/// List of ability templates rewarded for completing this tier.
		/// </summary>
		public List<BaseAbilityTemplate> AbilityRewards;

		/// <summary>
		/// List of ability events rewarded for completing this tier.
		/// </summary>
		public List<AbilityEvent> AbilityEventRewards;

		/// <summary>
		/// List of item templates rewarded for completing this tier.
		/// </summary>
		public List<BaseItemTemplate> ItemRewards;

		/// <summary>
		/// List of buff templates rewarded for completing this tier.
		/// </summary>
		public List<BaseBuffTemplate> BuffRewards;

		/// <summary>
		/// List of title strings rewarded for completing this tier.
		/// </summary>
		public List<string> TitleRewards;
	}
}