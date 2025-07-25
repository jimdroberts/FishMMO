using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Conditions
{
	[CreateAssetMenu(fileName = "HasGuildCondition", menuName = "FishMMO/Triggers/Conditions/Guild/Has Guild", order = 0)]
	public class HasGuildCondition : BaseCondition
	{
		[Tooltip("If true, the condition passes if the character is NOT in a guild.")]
		public bool InvertResult = false;

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter characterToCheck = initiator;
			if (eventData != null && eventData.TryGet(out CharacterHitEventData charTargetEventData) && charTargetEventData.Target != null)
			{
				characterToCheck = charTargetEventData.Target;
			}
			if (!characterToCheck.TryGet(out IGuildController guildController))
			{
				Log.Warning("HasGuildCondition", $"Character '{characterToCheck?.Name}' does not have a Guild Controller. Condition failed.");
				return false;
			}
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

		public override string GetFormattedDescription()
		{
			return InvertResult
				? "Requires the character to NOT be in a guild."
				: "Requires the character to be in a guild.";
		}
	}
}