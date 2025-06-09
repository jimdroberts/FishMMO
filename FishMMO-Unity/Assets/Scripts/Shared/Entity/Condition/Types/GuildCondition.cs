using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Guild Condition", menuName = "FishMMO/Conditions/Guild Condition", order = 1)]
	public class GuildCondition : BaseCondition<IPlayerCharacter>
	{
		public bool MustBeInGuild = true;

		public override bool Evaluate(IPlayerCharacter playerCharacter)
		{
			if (playerCharacter == null)
			{
				Debug.LogWarning($"Player character does not exist.");
				return false;
			}
			if (!playerCharacter.TryGet(out IGuildController guildController))
			{
				Debug.LogWarning($"Player character {playerCharacter.CharacterName} does not have an IGuildController.");
				return false;
			}

			// A player is considered "in a guild" if their guildController.ID is not 0.
			// (Assuming ID 0 means no guild)
			bool isInGuild = guildController.ID != 0;

			// If we require the player to be in a guild and they are, or
			// if we require the player NOT to be in a guild and they aren't.
			return MustBeInGuild == isInGuild;
		}
	}
}