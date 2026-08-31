using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that applies damage to a target character using a configurable value provider and a
	/// given damage attribute type.
	/// <para>
	/// Runs on the server, and on the client that OWNS the initiator — see
	/// <see cref="EcaAuthority.MayPredict(ICharacter, EventData)"/>. The caster draws its own
	/// number immediately through <see cref="PredictedCombatEvents"/> rather than waiting half a
	/// round trip for the server's report; the server's report then confirms it, or the prediction
	/// is greyed out when none arrives. Observers still wait to be told.
	/// </para>
	/// </summary>
	[Serializable]
	public class ApplyDamageAction : BaseAction
	{
		/// <summary>
		/// The value provider that determines the amount of damage to apply.
		/// </summary>
		[Tooltip("The value provider that determines the amount of damage to apply.")]
		[SerializeReference, SubclassSelector]
		public IIntValueProvider DamageValue;

		/// <summary>
		/// The attribute template associated with this damage type (e.g., 'Physical', 'Fire').
		/// Used to determine the element or type of damage applied.
		/// </summary>
		[Tooltip("The attribute template associated with this damage type (e.g., 'Physical', 'Fire').")]
		public DamageAttributeTemplate DamageAttributeTemplate;

		/// <summary>
		/// Applies damage to the target character using the computed value and attribute template.
		/// Runs on the server and on the client that owns the initiator — see the note on the class.
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
			if (DamageValue == null)
			{
				Log.Warning("DamageAction", "DamageValue provider is null.");
				return;
			}

			/* Drawn BEFORE the peer gate, never after — see AbilityObject.RNG. A provider may
			 * consume the ability object's generator, which every action in the event chain shares,
			 * so evaluating behind the gate advanced it only on the peers that pass and left an
			 * ungated action later in the chain reading a different number. */
			int amount = DamageValue.GetValue(initiator, eventData);

			if (!EcaAuthority.MayPredict(initiator, eventData))
			{
				return;
			}

			if (!TryResolveTarget(eventData, out ICharacter target))
			{
				return;
			}

			if (target.TryGet(out ICharacterDamageController defenderDamageController))
			{
				int applied = defenderDamageController.Damage(initiator, amount, DamageAttributeTemplate);

				/* The caster's own floating number, drawn now rather than on the server's report.
				 * The three cases that must NOT draw one are all in MayDrawPredictedNumber.
				 *
				 * Drawn from APPLIED, not from the raw provider amount. Damage() runs resistances and
				 * mitigation on this peer too (see the mitigation note in
				 * CharacterDamageController.Damage), and the server's report carries the mitigated
				 * number — so a label drawn from the raw amount showed a hit at full value on the
				 * caster's screen forever, because TryConfirm deliberately does not match amounts and
				 * the confirmation left the wrong number standing. A fully blocked hit returns zero
				 * and Predict refuses zero, which is the honest display. */
				if (applied > 0 && MayDrawPredictedNumber(initiator, eventData))
				{
					PredictedCombatEvents.Predict(initiator, target, applied, PredictedCombatEvents.Kind.Damage,
						DamageAttributeTemplate, UnityEngine.Time.unscaledTime);
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