using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Server.Conditions
{
	[CreateAssetMenu(fileName = "HasGuildCondition", menuName = "FishMMO/Conditions/Guild/Has Guild", order = 0)]
	public class HasGuildCondition : BaseCondition
	{
		[Tooltip("If true, the condition passes if the character is NOT in a party.")]
		public bool InvertResult = false;

		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			if (!initiator.TryGet(out IGuildController guildController))
			{
				Log.Warning($"HasGuildCondition: Initiator '{initiator?.Name}' does not have a Guild Controller. Condition failed.");
				return false;
			}

			bool isInGuild = guildController.ID != 0;

			if (InvertResult)
			{
				// If invertResult is true, we pass if they are NOT in a party
				if (isInGuild)
				{
					Log.Debug($"HasGuildCondition: Character '{initiator?.Name}' is in a guild, but 'invertResult' is true. Condition failed.");
				}
				return !isInGuild;
			}
			else
			{
				// If invertResult is false, we pass if they ARE in a party
				if (!isInGuild)
				{
					Log.Debug($"HasGuildCondition: Character '{initiator?.Name}' is not in a guild. Condition failed.");
				}
				return isInGuild;
			}
		}
	}
}