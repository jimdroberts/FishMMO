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
			/* Server only. State forwarding is off, so an observer never simulates another
			 * character and has nothing to predict here; the outcome reaches every peer through the
			 * authoritative paths (reconcile, observer broadcast). Running it locally as well would
			 * apply the effect twice on the peer that also happens to be the server, and produce a
			 * value on a client that the server never agreed to. */
			if (!EcaAuthority.IsServer(initiator, eventData))
			{
				return;
			}

			if (AmountToRemoveValue == null)
			{
				Log.Warning("ApplyDispelAction", "AmountToRemoveValue provider is null.");
				return;
			}

			if (!TryResolveTarget(eventData, out ICharacter target))
			{
				return;
			}

			if (target.TryGet(out IBuffController defenderBuffController))
			{
				/* The event's own generator, which is now always there: an event that was not handed
				 * one derives it from its initiator and tick rather than answering null. The old
				 * fallback to DeterministicRNG.Shared meant a dispel rolled off a process-wide stream
				 * seeded from Environment.TickCount, so which buffs it stripped was not reproducible
				 * and not agreed on. */
				DeterministicRNG rng = eventData != null ? eventData.RNG : new DeterministicRNG(EventData.DeriveSeed(0, 0u, 0));

				int amountToRemove = AmountToRemoveValue.GetValue(initiator, eventData);
				for (int i = 0; i < amountToRemove && defenderBuffController.Buffs.Count > 0; ++i)
				{
					defenderBuffController.RemoveRandom(rng, IncludeBuffs, IncludeDebuffs);
				}
			}
		}
	}
}