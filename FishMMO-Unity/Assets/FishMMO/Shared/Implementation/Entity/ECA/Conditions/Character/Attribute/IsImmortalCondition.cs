using System;
using FishMMO.Logging;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks if a character is immortal (cannot be killed), with optional inversion to check for mortality.
	/// </summary>
	[Serializable]
	public class IsImmortalCondition : BaseCondition
	{
		/// <inheritdoc />
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter characterToCheck = (eventData?.TargetCharacter ?? initiator);

			if (!characterToCheck.TryGet(out ICharacterDamageController damageController))
			{
				Log.Warning("IsImmortalCondition", $"EventData does not contain an ICharacterDamageController. Condition failed. (Character: {characterToCheck?.Name})");
				return false;
			}

			bool isImmortal = damageController.Immortal;
			if (!isImmortal)
			{
				Log.Debug("IsImmortalCondition", $"(Character: '{characterToCheck?.Name}') is mortal.");
			}
			return isImmortal;
		}
	}
}