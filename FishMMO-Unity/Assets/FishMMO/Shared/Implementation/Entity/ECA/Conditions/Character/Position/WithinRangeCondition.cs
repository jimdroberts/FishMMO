using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Passes when the evaluated character is within a distance of the initiator.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Position-sensitive, and that has consequences.</b> This is the first shipped condition
	/// whose answer depends on where characters ARE rather than on what state they hold, so it is
	/// the first that can disagree with the query that produced its candidate. Every selector that
	/// queries space evaluates its conditions inside the same lag-compensation scope as the query
	/// (see <c>TargetSelector.GatherRewound</c>), so used there this measures the same world the
	/// candidate came from.
	/// </para>
	/// <para>
	/// Used anywhere else — attached to a Trigger's top-level conditions, or to a condition's own
	/// <see cref="BaseCondition.TargetSelector"/> — it measures live server positions instead. That
	/// is not wrong, but it is a different question, and at 300 ms of latency the two answers differ
	/// by a couple of metres. Prefer a generous radius over a tight one for exactly that reason:
	/// this is a gate, not a hit test. A hit test belongs in a selector.
	/// </para>
	/// <para>
	/// Distance is measured between transform origins and compared squared, so no square root runs
	/// per candidate. <see cref="IgnoreVerticalDistance"/> makes it a cylinder rather than a sphere,
	/// which is usually what an authored "within 10 metres" means on terrain with any relief.
	/// </para>
	/// </remarks>
	[Serializable]
	public class WithinRangeCondition : BaseCondition
	{
		/// <summary>Maximum distance, in metres, between the initiator and the evaluated character.</summary>
		[Tooltip("Maximum distance in metres. Generous values are preferred; this is a gate, not a hit test.")]
		[Min(0f)]
		public float MaximumRange = 10f;

		/// <summary>
		/// Minimum distance, in metres. Zero means no minimum.
		/// </summary>
		/// <remarks>
		/// For abilities with a dead zone — a ranged attack that cannot be used point blank. A
		/// minimum above <see cref="MaximumRange"/> can never pass, which is a misconfiguration this
		/// does not silently repair: swapping them would make an authored mistake behave plausibly
		/// and hide itself.
		/// </remarks>
		[Tooltip("Minimum distance in metres. 0 for no minimum. Must be below Maximum Range to ever pass.")]
		[Min(0f)]
		public float MinimumRange = 0f;

		/// <summary>
		/// When true, distance is measured on the horizontal plane only.
		/// </summary>
		/// <remarks>
		/// A sphere centred on a caster reaches less far along the ground as the height difference
		/// grows, so a target on a ledge one metre up is out of a ten metre sphere at 9.95 metres
		/// horizontally. Authors almost always mean the cylinder.
		/// </remarks>
		[Tooltip("Measure horizontal distance only, ignoring height difference.")]
		public bool IgnoreVerticalDistance = true;

		/// <inheritdoc />
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter subject = eventData?.TargetCharacter ?? initiator;

			if (initiator == null || subject == null)
			{
				return false;
			}

			Transform from = initiator.Transform;
			Transform to = subject.Transform;
			if (from == null || to == null)
			{
				return false;
			}

			/* A character always satisfies a range check against itself. Reached whenever the
			 * event carries no target and the subject falls back to the initiator; answering
			 * "false" there would make a self-targeted ability fail its own range gate. */
			if (ReferenceEquals(from, to))
			{
				return MinimumRange <= 0f;
			}

			Vector3 delta = to.position - from.position;
			if (IgnoreVerticalDistance)
			{
				delta.y = 0f;
			}

			float squared = delta.sqrMagnitude;

			if (squared > MaximumRange * MaximumRange)
			{
				return false;
			}
			if (MinimumRange > 0f && squared < MinimumRange * MinimumRange)
			{
				return false;
			}
			return true;
		}
	}
}
