using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that forces an NPC's threat onto the initiator — the taunt primitive.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Attach to an ability's <see cref="AbilityOnHitEvent"/> to make it a taunt. Without this,
	/// an ability nominated as a "taunt" only changed which ability an NPC <em>preferred to
	/// cast</em>; it had no effect whatsoever on the victim's threat table, so
	/// <see cref="DefenderAttackingState.TauntAbilityTemplateIDs"/> could never actually pull
	/// anything off a squishier ally. This is the missing half.
	/// </para>
	/// <para>
	/// Server-only in effect: threat tables live on the server's <see cref="AIController"/> and
	/// are not replicated.
	/// </para>
	/// </remarks>
	[Serializable]
	public class ApplyTauntAction : BaseAction
	{
		/// <summary>
		/// Flat threat added to the target's table for the initiator.
		/// </summary>
		[Tooltip("Flat threat points added for the taunting character.")]
		public float ThreatPoints = 500f;

		/// <summary>
		/// When true, the taunt also guarantees the initiator ends up on top by topping their
		/// threat past the current highest entry plus <see cref="LeadOverHighest"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A flat bonus alone is unreliable in a long fight: after a minute of sustained damage
		/// the top threat entry can be far beyond any fixed number, so the taunt lands and nothing
		/// changes. This makes the guarantee explicit rather than hoping the number is big enough.
		/// </para>
		/// <para>
		/// <b>The guarantee is computed in SCORE space, not in raw points.</b> An NPC chooses its
		/// target with <c>AggressionController.GetThreatScore</c>, which multiplies raw points by a
		/// vulnerability factor for a wounded or out-of-mana character — so a taunt that merely put
		/// the taunter above the highest RAW entry still lost to a wounded ally on the very next
		/// re-evaluation, and <see cref="ForceImmediateTargetSwitch"/> hid it for exactly one
		/// switch. Comparing where the comparison actually happens is what makes this a guarantee
		/// rather than a strong suggestion.
		/// </para>
		/// </remarks>
		[Tooltip("Guarantee the taunter becomes the highest-threat target, not merely a higher one.")]
		public bool GuaranteeTopThreat = true;

		/// <summary>
		/// Extra threat placed above the previous highest entry when
		/// <see cref="GuaranteeTopThreat"/> is set.
		/// </summary>
		[Tooltip("Threat placed above the previous highest entry when guaranteeing top threat.")]
		public float LeadOverHighest = 100f;

		/// <summary>
		/// When true the target immediately switches to the taunter rather than waiting for its
		/// next scheduled re-evaluation.
		/// </summary>
		[Tooltip("Switch the target's focus immediately instead of waiting for its next re-evaluation.")]
		public bool ForceImmediateTargetSwitch = true;

		/// <summary>
		/// Applies the taunt to the resolved target.
		/// </summary>
		/// <param name="initiator">The taunting character.</param>
		/// <param name="eventData">Event data carrying the taunted target.</param>
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

			if (initiator == null)
			{
				return;
			}

			// Strict resolution: a taunt with no target must be a no-op, never a self-taunt.
			if (!TryResolveTarget(eventData, out ICharacter target) || target == initiator)
			{
				return;
			}

			if (!target.TryGet(out IAIController aiController))
			{
				// Players have no threat table; taunting one is a no-op by design.
				return;
			}

			AIController controller = aiController as AIController;
			if (controller == null || controller.Aggression == null)
			{
				return;
			}

			float points = ThreatPoints;

			if (GuaranteeTopThreat)
			{
				/* Worked in SCORE space and converted back, because score is what PickTarget reads.
				 *
				 * The table holds raw points and no character references, so it cannot evaluate
				 * another entry's score directly — but every entry's score is at most its raw points
				 * times MaximumVulnerabilityMultiplier, so clearing that bound clears every actual
				 * score whichever entry carries it. Conservative by up to that factor and never
				 * short, which is the direction a guarantee has to err in.
				 *
				 * The taunter's OWN multiplier is known exactly (we hold the character), so the
				 * required score is divided by it rather than bounded: a wounded taunter needs
				 * proportionally fewer raw points to reach the same score. */
				float highestRaw = controller.Aggression.GetHighestPoints(initiator.ID);
				float ceilingScore = highestRaw * controller.Aggression.MaximumVulnerabilityMultiplier;

				float taunterMultiplier = controller.Aggression.VulnerabilityMultiplier(initiator);
				if (taunterMultiplier <= 0f)
				{
					taunterMultiplier = 1f;
				}

				float requiredPoints = (ceilingScore + LeadOverHighest) / taunterMultiplier;
				float required = requiredPoints - controller.Aggression.GetPoints(initiator.ID);
				if (required > points)
				{
					points = required;
				}
			}

			if (points > 0f)
			{
				controller.Aggression.AddPoints(initiator.ID, points);
			}

			if (ForceImmediateTargetSwitch)
			{
				controller.ForceTarget(initiator);
			}
		}
	}
}
