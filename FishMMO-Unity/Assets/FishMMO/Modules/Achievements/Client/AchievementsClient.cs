using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// What the Achievements module adds to the client: the announcement of a completed tier, a floating label over the
	/// character (<see cref="ClientMilestoneDisplay.ShowMilestone"/>) and a system line in chat, the lasting record of it.
	/// </summary>
	/// <remarks>Moved out of <c>ClientCombatDisplay</c> with the module (2026-11-20).</remarks>
	public static class AchievementsClient
	{
		/// <summary>Subscribes once. Runs before the first scene loads; safe to run again.</summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void Install()
		{
			IAchievementController.OnCompleteAchievement -= OnCompleteAchievement;
			IAchievementController.OnCompleteAchievement += OnCompleteAchievement;
		}

		private static void OnCompleteAchievement(ICharacter character, AchievementTemplate template, AchievementTier tier)
		{
			if (character == null || template == null || tier == null)
			{
				return;
			}
			ClientMilestoneDisplay.ShowMilestone(character, "Achievement: " + template.Name, tier.TierCompleteMessage);

			// The floating label is gone in four seconds; the chat line is the record of it.
			ChatWindow.ShowSystem(string.IsNullOrEmpty(tier.TierCompleteMessage)
				? $"Achievement earned: {template.Name}."
				: $"Achievement earned: {template.Name}. {tier.TierCompleteMessage}");
		}
	}
}
