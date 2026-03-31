using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that dispels (removes) a specified number of buffs and/or debuffs from a target character.
	/// </summary>
	[Serializable]
	public class ApplyDispelAction : BaseAction
	{
		/// <summary>
		/// The value provider that determines the number of buffs and/or debuffs to remove.
		/// </summary>
		[Tooltip("The value provider that determines the number of buffs/debuffs to remove.")]
		[SerializeReference, SubclassSelector]
		public IIntValueProvider AmountToRemoveValue;

		/// <summary>
		/// Whether to include debuffs in the dispel operation.
		/// </summary>
		public bool IncludeDebuffs;

		/// <summary>
		/// Whether to include buffs in the dispel operation.
		/// </summary>
		public bool IncludeBuffs;

		/// <summary>
		/// Removes a computed number of buffs and/or debuffs from the target character.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing the target information.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (AmountToRemoveValue == null)
			{
				Log.Warning("ApplyDispelAction", "AmountToRemoveValue provider is null.");
				return;
			}

			ICharacter target = ResolveTarget(initiator, eventData);
			if (target == null)
			{
				return;
			}

			if (target.TryGet(out IBuffController defenderBuffController))
			{
				// Use deterministic RNG from CharacterHitEventData when available.
				DeterministicRNG rng = DeterministicRNG.Shared;
				if (eventData != null &&
					eventData.TryGet(out CharacterHitEventData hitData) &&
					hitData.RNG != null)
				{
					rng = hitData.RNG;
				}

				int amountToRemove = AmountToRemoveValue.GetValue(initiator, eventData);
				for (int i = 0; i < amountToRemove && defenderBuffController.Buffs.Count > 0; ++i)
				{
					defenderBuffController.RemoveRandom(rng, IncludeBuffs, IncludeDebuffs);
				}
			}
		}
	}
}