using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selects the caster's resolved target entity, validated by range — the EverQuest / WoW model.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why this exists.</b> Every other selector answers "what is in this volume", so its outcome
	/// is a function of where characters are at the moment the query runs. That makes it sensitive to
	/// the gap between where a client renders its peers and where the server holds them — measured at
	/// 0.45&#160;m on a same-city connection and 2.2&#160;m at 300&#160;ms. A raycast is the worst
	/// case, having no width to absorb any of it.
	/// </para>
	/// <para>
	/// This selector asks a different question: "who did the server decide the caster is aiming at".
	/// The answer is an entity reference, and the only positional test left is a range check with
	/// metres of tolerance. <b>The outcome stops being a function of peer position</b>, so it is
	/// correct at 8&#160;ms and at 300&#160;ms alike, with no rewinding and no history buffer. That
	/// is exactly how instant ranged attacks have always worked in EverQuest and WoW, and it is why
	/// those games never needed lag compensation for them.
	/// </para>
	/// <para>
	/// <b>Server authority.</b> The target is whatever <see cref="ITargetController.Current"/> holds,
	/// and on the server that was written by the server's own raycast from the replicated aim in
	/// <c>AbilityController.ResolveTargetAndSpawn</c>. The client supplies a direction; the server
	/// decides what is under it. A client cannot name its victim through this path.
	/// </para>
	/// </remarks>
	[Serializable]
	public class TargetedEntitySelector : TargetSelector
	{
		/// <summary>
		/// Maximum distance between caster and target for the ability to apply.
		/// </summary>
		/// <remarks>
		/// Re-checked here rather than trusted from acquisition time, because a target acquired at
		/// cast start can walk out of range during a wind-up. Generous by design — this is the only
		/// positional test in the path, and giving it metres of slack is what keeps the model
		/// latency-independent.
		/// </remarks>
		[Tooltip("Maximum caster-to-target distance. Generous by design; this is the only positional test.")]
		[Min(0f)]
		public float MaximumRange = 30f;

		/// <summary>
		/// When set, requires an unobstructed line from the caster to the target.
		/// </summary>
		/// <remarks>
		/// Off by default. The check is a raycast against world geometry — not against characters —
		/// so it does not reintroduce the peer-position sensitivity this selector exists to avoid.
		/// Terrain does not move, so no compensation is needed for it.
		/// </remarks>
		[Tooltip("Require unobstructed line of sight to the target. Tests world geometry only, never characters.")]
		public bool RequireLineOfSight;

		/// <summary>Layers treated as sight-blocking when <see cref="RequireLineOfSight"/> is set.</summary>
		[Tooltip("Layers that block line of sight. Should contain terrain and structures, not characters.")]
		public LayerMask LineOfSightBlockers;

		/// <summary>Vertical offset applied to both ends of the line-of-sight test.</summary>
		[Tooltip("Eye height used for the line of sight test, so it does not clip the ground.")]
		[Min(0f)]
		public float EyeHeight = 1.45f;

		/// <inheritdoc/>
		public override IEnumerable<GameObject> SelectTargets(EventData eventData)
		{
			// Replicate ticks run on every peer; resolving a hit there would apply it client-side.
			if (eventData != null && eventData.TryGet(out TickEventData tickData) && tickData.IsReplicateTick)
			{
				yield break;
			}

			ICharacter caster = eventData?.Initiator;
			if (caster == null)
			{
				yield break;
			}

			if (!caster.TryGet(out ITargetController targetController))
			{
				yield break;
			}

			Transform target = targetController.Current.Target;
			if (target == null)
			{
				yield break;
			}

			// Never let an ability resolve onto its own caster through this path.
			if (ReferenceEquals(target, caster.Transform))
			{
				yield break;
			}

			Vector3 casterPosition = caster.Transform.position;
			Vector3 targetPosition = target.position;

			if ((targetPosition - casterPosition).sqrMagnitude > MaximumRange * MaximumRange)
			{
				yield break;
			}

			if (RequireLineOfSight && IsSightBlocked(caster, casterPosition, targetPosition))
			{
				yield break;
			}

			GameObject targetObject = target.gameObject;
			if (AreConditionsMet(targetObject, eventData))
			{
				yield return targetObject;
			}
		}

		/// <summary>Returns true when world geometry stands between caster and target.</summary>
		private bool IsSightBlocked(ICharacter caster, Vector3 from, Vector3 to)
		{
			if (LineOfSightBlockers.value == 0)
			{
				return false;
			}

			Vector3 eye = from + Vector3.up * EyeHeight;
			Vector3 aim = to + Vector3.up * EyeHeight;
			Vector3 delta = aim - eye;
			float distance = delta.magnitude;
			if (distance <= 0.001f)
			{
				return false;
			}

			PhysicsScene physicsScene = caster.GameObject.scene.GetPhysicsScene();
			return physicsScene.Raycast(eye, delta / distance, out _, distance,
				LineOfSightBlockers, QueryTriggerInteraction.Ignore);
		}

		/// <inheritdoc/>
		public override string GetTooltipContribution()
			=> $"Current target, within {MaximumRange}m" + (RequireLineOfSight ? ", requires line of sight" : string.Empty);
	}
}
