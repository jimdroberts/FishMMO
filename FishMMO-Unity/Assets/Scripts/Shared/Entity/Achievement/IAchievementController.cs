using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for a character's achievement controller, handling achievement progress, queries, and related events.
	/// </summary>
	public interface IAchievementController : ICharacterBehaviour
	{
		/// <summary>
		/// Static event triggered when an achievement is completed (all tiers for a template are reached).
		/// <para>Parameters: ICharacter (the character), AchievementTemplate (the template), AchievementTier (the completed tier).</para>
		/// </summary>
		static Action<ICharacter, AchievementTemplate, AchievementTier> OnCompleteAchievement;

		/// <summary>
		/// Static event triggered when an achievement is updated (progress or tier changes).
		/// <para>Parameters: ICharacter (the character), Achievement (the updated achievement).</para>
		/// </summary>
		static Action<ICharacter, Achievement> OnUpdateAchievement;

		/// <summary>
		/// Dictionary of all achievements for the character, indexed by template ID.
		/// </summary>
		Dictionary<int, Achievement> Achievements { get; }

		/// <summary>
		/// Sets the achievement progress for a given template ID, tier, and value.
		/// </summary>
		/// <param name="templateID">The template ID of the achievement.</param>
		/// <param name="tier">The tier to set.</param>
		/// <param name="value">The value to set.</param>
		/// <param name="skipEvent">If true, does not trigger the update event.</param>
		void SetAchievement(int templateID, byte tier, uint value, bool skipEvent = false);

		/// <summary>
		/// Attempts to get an achievement by template ID.
		/// </summary>
		/// <param name="templateID">The template ID to look up.</param>
		/// <param name="achievement">The resulting achievement if found.</param>
		/// <returns>True if the achievement exists, false otherwise.</returns>
		bool TryGetAchievement(int templateID, out Achievement achievement);

		/// <summary>
		/// Increments the progress of an achievement by a specified amount, handling tier advancement and rewards.
		/// </summary>
		/// <param name="template">The achievement template to increment.</param>
		/// <param name="amount">The amount to increment the achievement's value by.</param>
		void Increment(AchievementTemplate template, uint amount);
	}
}