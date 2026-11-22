using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// True when the initiator stands behind the event's target: inside an arc of <see cref="ArcDegrees"/> centred on the
	/// direction the TARGET faces away from. A backstab's gate.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The other way round from <see cref="IsWithinFacingAngleCondition"/>, which asks whether the target is in front of the
	/// initiator. Measured flat (height does not matter), as a cosine against the target's back, from live positions: the
	/// server resolves the hit and its answer is the one that counts; the caster's client predicts the same from what it
	/// sees.
	/// </para>
	/// <para>
	/// A target with no target character (scenery) or no horizontal facing has no back: false. Standing exactly on the
	/// target has no direction to measure: false, because a backstab must be earned.
	/// </para>
	/// </remarks>
	[Serializable]
	public class IsBehindTargetCondition : BaseCondition
	{
		/// <summary>Total arc behind the target, degrees: 120 allows 60 either side of straight behind.</summary>
		[Tooltip("Total arc behind the target in degrees. 120 allows 60 degrees either side of straight behind.")]
		[Range(1f, 359f)]
		public float ArcDegrees = 120f;

		/// <inheritdoc />
		public override bool Evaluate(ICharacter initiator, EventData eventData)
		{
			ICharacter target = eventData?.TargetCharacter;
			if (initiator == null || target == null || ReferenceEquals(initiator, target) ||
				initiator.Transform == null || target.Transform == null)
			{
				return false;
			}
			return IsBehind(target.Transform.forward, initiator.Transform.position - target.Transform.position, ArcDegrees);
		}

		/// <summary>
		/// True when <paramref name="toInitiator"/> (target to initiator) lies within <paramref name="arcDegrees"/> of the
		/// direction opposite <paramref name="targetForward"/>. Pure.
		/// </summary>
		public static bool IsBehind(Vector3 targetForward, Vector3 toInitiator, float arcDegrees)
		{
			targetForward.y = 0f;
			toInitiator.y = 0f;
			if (targetForward.sqrMagnitude < 1e-6f || toInitiator.sqrMagnitude < 1e-6f)
			{
				return false;
			}
			float halfAngleCos = Mathf.Cos(Mathf.Clamp(arcDegrees, 0f, 360f) * 0.5f * Mathf.Deg2Rad);
			return Vector3.Dot(-targetForward.normalized, toInitiator.normalized) >= halfAngleCos;
		}
	}
}
