using System;
using FishMMO.Logging;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if a character is alive (health > 0), with optional inversion to check for death.
	/// </summary>
	[Serializable]
	public class IsCharacterAliveCondition : BaseCondition
	{
		/// <inheritdoc />
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter characterToCheck = (eventData?.TargetCharacter ?? initiator);

			if (!characterToCheck.TryGet(out ICharacterAttributeController attributeController))
			{
				Log.Warning("IsCharacterAliveCondition", $"Character '{characterToCheck?.Name}' does not have an ICharacterAttributeController. Condition failed.");
				return false;
			}

			if (!attributeController.TryGetHealthAttribute(out CharacterResourceAttribute healthAttribute))
			{
				Log.Warning("IsCharacterAliveCondition", $"Character '{characterToCheck?.Name}' does not have a Health Resource Attribute. Condition failed.");
				return false;
			}

			bool isAlive = healthAttribute.CurrentValue > 0;
			if (!isAlive)
			{
				Log.Debug("IsCharacterAliveCondition", $"Character '{characterToCheck?.Name}' is dead (health <= 0).");
			}
			return isAlive;
		}
	}
}