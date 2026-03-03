using System;
using FishMMO.Logging;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if a character is in a guild, with optional inversion.
	/// </summary>
	[Serializable]
	public class HasGuildCondition : BaseCondition
	{
		/// <summary>
		/// If true, the condition passes if the character is NOT in a guild.
		/// </summary>
		[Tooltip("If true, the condition passes if the character is NOT in a guild.")]
		public bool InvertResult = false;

		/// <summary>
		/// Evaluates whether the character (or event target) is in a guild, or not in a guild if <see cref="InvertResult"/> is true.
		/// </summary>
		/// <param name="initiator">The character to check, or the fallback if no event target is present.</param>
		/// <param name="eventData">Optional event data that may provide a different character to check.</param>
		/// <returns>True if the character is (or is not) in a guild, depending on <see cref="InvertResult"/>; otherwise, false.</returns>
		/// <remarks>
		/// This method checks for a guild controller and evaluates the guild membership. If <see cref="InvertResult"/> is true, the logic is inverted.
		/// </remarks>
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			// Determine which character to check: use the event target if available, otherwise use the initiator.
			ICharacter characterToCheck = ResolveTarget(initiator, eventData);
			// Check if the character has a guild controller.
			if (!characterToCheck.TryGet(out IGuildController guildController))
			{
				Log.Warning("HasGuildCondition", $"Character '{characterToCheck?.Name}' does not have a Guild Controller. Condition failed.");
				return false;
			}
			// A character is considered in a guild if their guild ID is not zero.
			bool isInGuild = guildController.ID != 0;
			if (InvertResult)
			{
				if (isInGuild)
				{
					Log.Debug("HasGuildCondition", $"Character '{characterToCheck?.Name}' is in a guild, but 'invertResult' is true. Condition failed.");
				}
				return !isInGuild;
			}
			else
			{
				if (!isInGuild)
				{
					Log.Debug("HasGuildCondition", $"Character '{characterToCheck?.Name}' is not in a guild. Condition failed.");
				}
				return isInGuild;
			}
		}

	}
}