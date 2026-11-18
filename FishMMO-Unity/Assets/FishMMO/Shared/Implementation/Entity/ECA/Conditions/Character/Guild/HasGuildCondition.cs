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
		/// <inheritdoc />
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter characterToCheck = (eventData?.TargetCharacter ?? initiator);
			if (!characterToCheck.TryGet(out IGuildController guildController))
			{
				Log.Warning("HasGuildCondition", $"Character '{characterToCheck?.Name}' does not have a Guild Controller. Condition failed.");
				return false;
			}
			bool isInGuild = guildController.ID != 0;
			if (!isInGuild)
			{
				Log.Debug("HasGuildCondition", $"Character '{characterToCheck?.Name}' is not in a guild.");
			}
			return isInGuild;
		}
	}
}