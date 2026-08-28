using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that generates threat on nearby hostile NPCs without dealing damage.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The designer-facing hook for "this cast drew attention". Two uses:
	/// </para>
	/// <list type="bullet">
	///   <item>
	///     <b>Flat</b> — a high-threat ability adds a fixed number of points.
	///   </item>
	///   <item>
	///     <b>Resource-weighted</b> — the cost of the cast is fed through each NPC's
	///     <see cref="AggressionController.ResourceWeight"/>, so a caster burning mana near a
	///     pack draws proportionally more attention than one chipping away.
	///   </item>
	/// </list>
	/// <para>
	/// The resource-weighted path is what finally gives <see cref="AggressionController.ResourceWeight"/>
	/// and <c>RecordResourceSpent</c> a caller. Both existed, were serialized, and were documented
	/// as "points per resource point spent casting near the NPC" — but nothing in the project ever
	/// invoked them, so tuning that weight had no effect at all.
	/// </para>
	/// <para>
	/// Only NPCs already in combat are affected: <see cref="AggressionState.RecordResourceSpent"/>
	/// ignores an NPC with an empty threat table, so casting near an unaware mob does not pull it.
	/// Server-only in effect — threat tables are not replicated.
	/// </para>
	/// </remarks>
	[Serializable]
	public class ApplyThreatAction : BaseAction
	{
		/// <summary>
		/// Radius around the initiator within which hostile NPCs notice the cast.
		/// </summary>
		[Tooltip("Radius around the caster within which hostile NPCs gain threat.")]
		public float Radius = 20f;

		/// <summary>
		/// Physics layers to search for NPCs.
		/// </summary>
		[Tooltip("Physics layers to search for NPCs.")]
		public LayerMask NPCLayers;

		/// <summary>
		/// Flat threat points added to every affected NPC. Applied in addition to
		/// <see cref="ResourceSpent"/>.
		/// </summary>
		[Tooltip("Flat threat added to each affected NPC.")]
		public float ThreatPoints = 0f;

		/// <summary>
		/// Amount of resource this cast is treated as having spent. Each NPC scales it by its own
		/// <see cref="AggressionController.ResourceWeight"/>.
		/// </summary>
		[Tooltip("Resource spent by this cast, weighted per-NPC by its ResourceWeight.")]
		public int ResourceSpent = 0;

		/// <summary>
		/// Reusable overlap buffer. Safe to share: the sweep is fully consumed inside a single
		/// synchronous <see cref="Execute"/> call.
		/// </summary>
		private static readonly Collider[] hits = new Collider[32];

		/// <summary>
		/// Applies threat to hostile NPCs around the initiator.
		/// </summary>
		/// <param name="initiator">The casting character.</param>
		/// <param name="eventData">Unused; threat is applied by proximity, not to a named target.</param>
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

			if (initiator == null || Radius <= 0f)
			{
				return;
			}

			if (ThreatPoints <= 0f && ResourceSpent <= 0)
			{
				return;
			}

			if (!initiator.TryGet(out IFactionController initiatorFaction))
			{
				return;
			}

			PhysicsScene physicsScene = initiator.GameObject.scene.GetPhysicsScene();

			int count = physicsScene.OverlapSphere(
				initiator.Transform.position,
				Radius,
				hits,
				NPCLayers,
				QueryTriggerInteraction.Ignore);

			for (int i = 0; i < count && i < hits.Length; ++i)
			{
				Collider collider = hits[i];
				if (collider == null || collider == initiator.Collider)
				{
					continue;
				}

				ICharacter candidate = collider.GetComponent<ICharacter>();
				if (candidate == null || candidate == initiator)
				{
					continue;
				}

				// Only hostiles care. An ally noticing your mana bar is not a threat mechanic.
				if (!candidate.TryGet(out IFactionController candidateFaction) ||
					candidateFaction.GetAllianceLevel(initiatorFaction) != FactionAllianceLevel.Enemy)
				{
					continue;
				}

				if (!candidate.TryGet(out IAIController aiController))
				{
					continue;
				}

				AIController controller = aiController as AIController;
				if (controller == null || controller.AggressionState == null)
				{
					continue;
				}

				if (ResourceSpent > 0)
				{
					controller.AggressionState.RecordResourceSpent(initiator.ID, ResourceSpent);
				}

				if (ThreatPoints > 0f && controller.Aggression != null && controller.Aggression.HasAggression)
				{
					controller.Aggression.AddPoints(initiator.ID, ThreatPoints);
				}
			}
		}
	}
}
