using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Passes when the evaluated character lies within an arc of the initiator's facing — a
	/// "must be in front of you" gate, or with <see cref="BaseCondition.Invert"/>, a backstab gate.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Position-sensitive in the same way <see cref="WithinRangeCondition"/> is, with one extra
	/// term: it also depends on the initiator's ROTATION, which a client changes far faster than it
	/// changes position. A tight arc is therefore much more latency-sensitive than a tight radius —
	/// a player spinning at 180 degrees a second sweeps a 45 degree arc past a target in a quarter
	/// of a second, well inside a high-latency round trip. Prefer wide arcs, and treat a narrow one
	/// as a design decision about feel rather than a precision instrument.
	/// </para>
	/// <para>
	/// Measured against the initiator's transform forward on the horizontal plane. Height is ignored
	/// deliberately: a target directly above or below is at an extreme vertical angle but is not
	/// "behind" you in any sense an author means.
	/// </para>
	/// </remarks>
	[Serializable]
	public class IsWithinFacingAngleCondition : BaseCondition
	{
		/// <summary>
		/// Total width of the arc in degrees, centred on the initiator's forward.
		/// </summary>
		/// <remarks>
		/// The FULL angle, not the half angle: 90 means 45 degrees to each side. Authors read a cone
		/// as its total spread, and halving it here rather than at the call site is what stops the
		/// same number meaning two things in two places. 360 or more always passes.
		/// </remarks>
		[Tooltip("Total arc width in degrees, centred on forward. 90 allows 45 degrees to each side.")]
		[Range(0f, 360f)]
		public float ArcDegrees = 90f;

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

			// A character is trivially within its own facing arc; the delta below would be zero.
			if (ReferenceEquals(from, to))
			{
				return true;
			}

			if (ArcDegrees >= 360f)
			{
				return true;
			}
			if (ArcDegrees <= 0f)
			{
				return false;
			}

			Vector3 delta = to.position - from.position;
			delta.y = 0f;
			if (delta.sqrMagnitude <= 0.0001f)
			{
				// Standing on top of each other: there is no direction to measure, so the arc
				// cannot exclude them. Answering false here would make a melee cone miss a target
				// that had walked exactly into the caster.
				return true;
			}

			Vector3 forward = from.forward;
			forward.y = 0f;
			if (forward.sqrMagnitude <= 0.0001f)
			{
				// A character with no horizontal facing (looking straight up or down) has no arc to
				// test against. Treated as unable to satisfy a directional gate rather than as
				// satisfying every one of them.
				return false;
			}

			/* Compared as a cosine rather than through Vector3.Angle, which runs an acos per
			 * candidate. The half angle is what a dot product against forward describes. */
			float halfAngleCos = Mathf.Cos(ArcDegrees * 0.5f * Mathf.Deg2Rad);
			float dot = Vector3.Dot(forward.normalized, delta.normalized);

			return dot >= halfAngleCos;
		}
	}
}
