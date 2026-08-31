using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that restores health to a target character using a configurable value provider.
	/// </summary>
	[Serializable]
	public class ApplyHealAction : BaseAction
	{
		/// <summary>
		/// The value provider that determines the amount of health to restore.
		/// </summary>
		[Tooltip("The value provider that determines the amount of health to restore.")]
		[SerializeReference, SubclassSelector]
		public IIntValueProvider HealValue;

		/// <summary>
		/// Restores health to the target character using the computed value.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">The event data containing the target information.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			/* The server, or the client that OWNS the initiator — see EcaAuthority.MayPredict.
			 *
			 * This was server-only, which meant a player's own hit moved nothing on their screen
			 * until the server's report came back: hit, pause, then the bar drops. The caster has
			 * predicted the cast and owns the input that produced it, so it is the one peer with
			 * something to predict from; an observer still answers false and waits to be told.
			 *
			 * Safe because the authoritative consequences self-gate one level down —
			 * CharacterDamageController.Kill, QueueCombatEvent and RecordCombatContribution each
			 * return early unless IsServerStarted. A predicted hit moves a bar; it cannot kill
			 * anybody, emit a combat report, or award loot rights. The server's own resource
			 * broadcast overwrites the predicted value rather than accumulating on top of it, so a
			 * misprediction heals itself on the next push instead of drifting. */
			if (HealValue == null)
			{
				Log.Warning("HealAction", "HealValue provider is null.");
				return;
			}

			/* Drawn BEFORE the peer gate, never after — see AbilityObject.RNG. */
			int amount = HealValue.GetValue(initiator, eventData);

			if (!EcaAuthority.MayPredict(initiator, eventData))
			{
				return;
			}

			if (!TryResolveTargetOrInitiator(initiator, eventData, out ICharacter target))
			{
				return;
			}

			if (target.TryGet(out ICharacterDamageController defenderDamageController))
			{
				int applied = defenderDamageController.Heal(initiator, amount);

				/* The healer's own number, drawn now. See MayDrawPredictedNumber for the three guards.
				 * Drawn from APPLIED: a heal that had no effect (dead or already-full target) sends no
				 * combat report, so a label drawn from the raw amount could only ever grey out. */
				if (applied > 0 && MayDrawPredictedNumber(initiator, eventData))
				{
					PredictedCombatEvents.Predict(initiator, target, applied, PredictedCombatEvents.Kind.Heal,
						null, UnityEngine.Time.unscaledTime);
				}
			}
		}

		/// <summary>
		/// True when this peer should draw its own predicted number for this effect.
		/// </summary>
		/// <remarks>
		/// Three conditions, and each removes a different duplicate. Not the server, which has no
		/// display and whose report is what every other client draws from. Not a replayed tick, or a
		/// reconcile would draw one number per tick it re-simulates. And not a hit the SERVER
		/// resolved and told us about: that hit has a <c>CombatEventBroadcast</c> of its own already
		/// in flight, so predicting it produces two labels for one hit whenever the unreliable
		/// report wins the race against the reliable hit message. See
		/// <see cref="AbilityCollisionEventData.IsAuthoritativeEcho"/>.
		/// </remarks>
		private static bool MayDrawPredictedNumber(ICharacter initiator, EventData eventData)
		{
			if (EcaAuthority.IsServer(initiator, eventData) || IsReplayTick(eventData))
			{
				return false;
			}
			return !(eventData != null &&
				eventData.TryGet(out AbilityCollisionEventData collision) &&
				collision.IsAuthoritativeEcho);
		}

	}
}