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
		/// Server only — see the note on the class.
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
			if (!EcaAuthority.MayPredict(initiator, eventData))
			{
				return;
			}

			if (DamageValue == null)
			{
				Log.Warning("DamageAction", "DamageValue provider is null.");
				return;
			}

			if (!TryResolveTarget(eventData, out ICharacter target))
			{
				return;
			}

			if (target.TryGet(out ICharacterDamageController defenderDamageController))
			{
				int amount = DamageValue.GetValue(initiator, eventData);
				defenderDamageController.Damage(initiator, amount, DamageAttributeTemplate);

				/* The caster's own floating number, drawn now rather than on the server's report.
				 *
				 * Only off the server: the server has no display, and its report is what every
				 * OTHER client draws from. Only on a real tick, never a replayed one — a reconcile
				 * replays every tick since the last correction, and drawing a number per replayed
				 * tick is the visual spam PlayFXAction guards against for the same reason. */
				if (!EcaAuthority.IsServer(initiator, eventData) && !IsReplayTick(eventData))
				{
					PredictedCombatEvents.Predict(target, amount, PredictedCombatEvents.Kind.Damage,
						DamageAttributeTemplate, UnityEngine.Time.unscaledTime);
				}
			}
		}
	}
}