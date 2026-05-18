using System;
using FishMMO.Logging;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if a character is an NPC, with optional inversion.
	/// </summary>
	[Serializable]
	public class IsCharacterNPCCondition : BaseCondition
	{
		/// <inheritdoc />
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter characterToCheck = (eventData?.TargetCharacter ?? initiator);
			bool isNPC = characterToCheck is NPC;
			if (!isNPC)
			{
				Log.Debug("IsCharacterNPCCondition", $"Character '{characterToCheck?.Name}' is not an NPC.");
			}
			return isNPC;
		}
	}
}