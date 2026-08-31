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
		/// Salt distinguishing this action's stream from any other consumer of the same event.
		/// </summary>
		/// <remarks>
		/// A constant, deliberately. The seed must be a function of things every peer agrees on —
		/// the initiator's network id and the event's tick — and nothing else. Distinct from
		/// <c>RandomTargetSelector</c>'s salt so a dispel and a random selection in one event chain
		/// walk different sequences rather than the same one.
		/// </remarks>
		private const int DispelSelectionSalt = 0x4453_504C; // "DSPL"

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

			/* Drawn BEFORE the peer gate, never after — see AbilityObject.RNG. A provider may
			 * consume the ability object's generator, which every action in the event chain shares,
			 * so evaluating it behind the gate advanced it only on the peers that pass. The null
			 * guard above may precede it: an authoring fault answers the same on every peer,
			 * whereas the gate and the target resolution below do not. */
			int amountToRemove = AmountToRemoveValue.GetValue(initiator, eventData);

			/* Server only. State forwarding is off, so an observer never simulates another
			 * character and has nothing to predict here; the outcome reaches every peer through the
			 * authoritative paths (reconcile, observer broadcast). Running it locally as well would
			 * apply the effect twice on the peer that also happens to be the server, and produce a
			 * value on a client that the server never agreed to. */
			if (!EcaAuthority.IsServer(initiator, eventData))
			{
				return;
			}

			if (!TryResolveTarget(eventData, out ICharacter target))
			{
				return;
			}

			if (target.TryGet(out IBuffController defenderBuffController))
			{
				/* This event chain's OWN stream, never the shared generator, because this action is
				 * server-only.
				 *
				 * eventData.RNG is threaded onto an ability object's event payloads and is advanced
				 * by side effect, so drawing from it behind a peer gate advances it on the server
				 * alone — and an ungated action later in the same chain (AbilityForkHitAction) then
				 * reads a different number, putting an observer's copy of a forking projectile on a
				 * heading the server never took. That is the rule stated on AbilityObject.RNG, and
				 * a dispel is its worst shape: the loop below draws a VARIABLE number of times,
				 * bounded by an authored value and by how many buffs the victim happens to be
				 * carrying, so the two streams do not merely differ by one draw.
				 *
				 * Hoisting the draws above the gate is not available here the way it was for
				 * ConsumeResourceAction — removing buffs IS the server-only effect, not a decision
				 * taken once a value is known. So the effect takes its own stream instead.
				 *
				 * IndependentRNG rather than DeriveRNG: the latter is a pure factory and returns a
				 * fresh generator every call, so the loop below would draw the SAME index every
				 * iteration and strip one buff repeatedly. Memoised per (event chain, salt), the
				 * sequence advances exactly as the shared one would. Seeded from values every peer
				 * agrees on, so which buffs are stripped stays reproducible run to run — all a
				 * server-only roll ever needed. */
				DeterministicRNG rng = eventData != null
					? eventData.IndependentRNG(DispelSelectionSalt)
					: new DeterministicRNG(EventData.DeriveSeed(0, 0u, DispelSelectionSalt));

				for (int i = 0; i < amountToRemove && defenderBuffController.Buffs.Count > 0; ++i)
				{
					defenderBuffController.RemoveRandom(rng, IncludeBuffs, IncludeDebuffs);
				}
			}
		}
	}
}