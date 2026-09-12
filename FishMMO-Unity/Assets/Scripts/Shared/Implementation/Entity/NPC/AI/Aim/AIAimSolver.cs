using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The point on a target's body an NPC aims at.
	/// </summary>
	public enum AIAimPoint
	{
		/// <summary>
		/// The centre of the target's collider — mid-torso. The default.
		/// </summary>
		/// <remarks>
		/// This is what "aim at the player center" means literally: a body centre is inside the
		/// collider from every approach angle, so a shot that lands there is a hit regardless of
		/// which side of the target the NPC is standing on.
		/// </remarks>
		Center = 0,

		/// <summary>
		/// Just below the top of the target's collider — the head.
		/// </summary>
		Head = 1,

		/// <summary>
		/// The base of the target's collider, at its feet. For abilities that want an impact at
		/// ground level.
		/// </summary>
		Feet = 2,
	}

	/// <summary>
	/// The pure arithmetic behind an NPC's aim: which point to aim at, how accurately, and with how
	/// much error.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Static and free of scene, tick and controller state, so the whole of an aim profile's
	/// behaviour is assertable in an EditMode test — the same split
	/// <c>AICombatDecision</c> and <c>AIKiteBudget</c> use. Every value here is a function of its
	/// arguments; nothing is remembered between calls, and the callers own all of the state.
	/// </para>
	/// <para>
	/// <b>Why the aim point is resolved per call rather than cached.</b> The previous
	/// implementation cached the target's collider half-height against the target's transform and
	/// only re-resolved it when the target <em>identity</em> changed. A collider that was not yet
	/// resolvable on the first sight of a target — which is normal, since the model is instantiated
	/// asynchronously — cached a height of zero and aimed at the feet for the entire fight. Reading
	/// the bounds on each call cannot latch: there is no remembered value to go stale.
	/// </para>
	/// </remarks>
	public static class AIAimSolver
	{
		/// <summary>
		/// Squared distance below which an aim direction is treated as degenerate.
		/// </summary>
		/// <remarks>
		/// A direction this short has no meaningful orientation, so the caller keeps its previous
		/// rotation instead of writing garbage. The old code normalised first and then tested the
		/// squared length, which — a normalised vector being unit length by definition — could only
		/// ever fail for an exactly-zero input. The test is honest about what it is for now.
		/// </remarks>
		public const float MinimumAimDistanceSqr = 0.0001f;

		/// <summary>
		/// Mid-torso height used when a target has no collider.
		/// </summary>
		/// <remarks>
		/// Matches the centre of the 1.8 m capsule every character prefab uses, so the fallback
		/// lands where the collider's centre would have.
		/// </remarks>
		private const float FallbackTorsoHeight = 0.9f;

		/// <summary>
		/// Eye height used when a target with no collider is aimed at the head.
		/// </summary>
		/// <remarks>
		/// Deliberately equal to <c>CharacterAimOrigin</c>'s fallback eye height. An NPC aiming at
		/// another NPC's head and an NPC aiming from its own eye refer to the same height on the
		/// same rig, and two constants that must agree should be one — but they live in assemblies'
		/// worth of different callers, so the equality is asserted by a test instead.
		/// </remarks>
		private const float FallbackHeadHeight = 1.45f;

		/// <summary>
		/// Distance below the top of a collider the head aim point sits at.
		/// </summary>
		private const float HeadInsetFromTop = 0.15f;

		/// <summary>
		/// Returns the world-space point an NPC should aim at on a target.
		/// </summary>
		/// <remarks>
		/// <b>Why a collider and not a half-height.</b> The half-height form asked the caller to
		/// have already decided where the body's centre was, which is the decision this makes. Taking
		/// the collider whole also means a target whose collider changes shape — a crouch, a form
		/// change — is aimed at correctly on the next tick for free.
		/// </remarks>
		/// <param name="targetPosition">The target's root position, at its feet. Used when there is no collider.</param>
		/// <param name="targetCollider">The target's collider, or null.</param>
		/// <param name="aimPoint">Which point on the body to aim at.</param>
		/// <returns>The aim point in world space.</returns>
		public static Vector3 ResolveAimPoint(Vector3 targetPosition, Collider targetCollider, AIAimPoint aimPoint)
		{
			if (targetCollider == null)
			{
				switch (aimPoint)
				{
					case AIAimPoint.Head:
						return targetPosition + Vector3.up * FallbackHeadHeight;
					case AIAimPoint.Feet:
						return targetPosition;
					default:
						return targetPosition + Vector3.up * FallbackTorsoHeight;
				}
			}

			Bounds bounds = targetCollider.bounds;

			switch (aimPoint)
			{
				case AIAimPoint.Head:
					/* Clamped against the bottom of the collider so a collider shorter than the inset
					 * — a prone target, a small critter — cannot produce a head below its own feet. */
					return new Vector3(bounds.center.x,
						Mathf.Max(bounds.min.y, bounds.max.y - HeadInsetFromTop),
						bounds.center.z);
				case AIAimPoint.Feet:
					return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
				default:
					return bounds.center;
			}
		}

		/// <summary>
		/// Returns how long a shot should be led by, from the target's distance and the
		/// projectile's speed.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Derived from the ability, not authored per NPC.</b> An earlier design put an absolute
		/// lead in seconds on the aim profile, which cannot be right for two abilities of different
		/// speeds held by the same NPC: the same lead over-leads the fast one and under-leads the
		/// slow one. The flight time is a property of the shot, so it is computed from the shot.
		/// </para>
		/// <para>
		/// <b>Why <paramref name="projectileSpeed"/> is guarded.</b> An ability with no travel speed
		/// — a melee swing, an instant hit, a self-buff — has no flight time to lead, and dividing
		/// by its zero speed would produce an infinite lead. The guard is what makes this safe to
		/// call for every ability in a spellbook without classifying them first.
		/// </para>
		/// </remarks>
		/// <param name="distance">Distance from the aim origin to the aim point.</param>
		/// <param name="projectileSpeed">The ability's travel speed, or 0 for an instant ability.</param>
		/// <param name="leadAccuracy">0-1 authoring dial. 0 leads not at all, 1 leads perfectly.</param>
		/// <returns>Seconds of flight time to aim ahead by, or 0.</returns>
		public static float ResolveLeadTime(float distance, float projectileSpeed, float leadAccuracy)
		{
			if (projectileSpeed <= 0f || distance <= 0f)
			{
				return 0f;
			}

			return (distance / projectileSpeed) * Mathf.Clamp01(leadAccuracy);
		}

		/// <summary>
		/// Returns the world-space offset that leads a moving target.
		/// </summary>
		/// <param name="targetVelocity">The target's velocity in units per second.</param>
		/// <param name="leadSeconds">Seconds of flight time to aim ahead by.</param>
		/// <returns>The offset to add to the aim point.</returns>
		public static Vector3 ResolveLeadOffset(Vector3 targetVelocity, float leadSeconds)
		{
			if (leadSeconds <= 0f)
			{
				return Vector3.zero;
			}

			return targetVelocity * leadSeconds;
		}

		/// <summary>
		/// Returns the accuracy an NPC has reached after tracking a target for a while.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The curve is a smoothstep rather than a straight line for two reasons. It eases in, so a
		/// just-acquired target is meaningfully hard to hit for a beat instead of being at
		/// three-quarters accuracy on the first tick. And it eases out, so the last part of the ramp
		/// does not visibly snap from imperfect to perfect — which is the moment a player would
		/// notice that the misses were scripted.
		/// </para>
		/// <para>
		/// A <paramref name="lockSeconds"/> of zero or less means no ramp at all: the NPC is at its
		/// authored ceiling immediately. That is the profile that reproduces the old perfect-aim
		/// behaviour and is what a profile with nothing but a spread value degenerates to.
		/// </para>
		/// </remarks>
		/// <param name="lockedSeconds">Seconds the current target has been continuously held.</param>
		/// <param name="lockSeconds">Seconds of tracking required to reach full accuracy.</param>
		/// <param name="baseAccuracy">The 0-1 ceiling the ramp climbs to.</param>
		/// <returns>Accuracy in 0-1, where 1 is perfectly on target.</returns>
		public static float ResolveAccuracy(float lockedSeconds, float lockSeconds, float baseAccuracy)
		{
			float ceiling = Mathf.Clamp01(baseAccuracy);

			if (lockSeconds <= 0f)
			{
				return ceiling;
			}

			float t = Mathf.Clamp01(lockedSeconds / lockSeconds);
			return ceiling * (t * t * (3f - 2f * t));
		}

		/// <summary>
		/// Returns the angular spread a given accuracy produces.
		/// </summary>
		/// <param name="spreadDegrees">The profile's worst-case spread, at zero accuracy.</param>
		/// <param name="accuracy">Accuracy in 0-1 from <see cref="ResolveAccuracy"/>.</param>
		/// <returns>Spread in degrees, from <paramref name="spreadDegrees"/> down to 0.</returns>
		public static float ResolveSpread(float spreadDegrees, float accuracy)
		{
			if (spreadDegrees <= 0f)
			{
				return 0f;
			}

			return spreadDegrees * (1f - Mathf.Clamp01(accuracy));
		}

		/// <summary>
		/// Builds the angular error applied to a cast.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Why the error is a rotation and not noise.</b> This is rolled once per cast and held
		/// for the whole cast, so an inaccurate NPC tracks its target continuously and misses by a
		/// fixed angle. Re-rolling per tick would average the error out over a fast projectile's
		/// flight and produce a shower of near-misses that reads as broken; a held angle reads as an
		/// archer who is aiming at you and is not very good. It is also what makes the error
		/// visible at all — a per-tick wobble of a degree or two is invisible at range, where a held
		/// degree is metres.
		/// </para>
		/// <para>
		/// <b>Why the rolls are taken as a unit disc.</b> The two values arrive independently from
		/// the NPC's seeded RNG, and independent uniform pairs cover a <em>square</em>, whose corners
		/// reach sqrt(2) times further than its edges — so a "2 degree" spread would occasionally
		/// miss by nearly 3. Folding the pair into a disc before scaling makes
		/// <paramref name="spreadDegrees"/> the true worst case in every direction.
		/// </para>
		/// </remarks>
		/// <param name="spreadDegrees">Worst-case angular error in degrees.</param>
		/// <param name="yawRoll">First roll in -1..1. Values outside are clamped.</param>
		/// <param name="pitchRoll">Second roll in -1..1. Values outside are clamped.</param>
		/// <returns>A rotation to apply on top of the solved aim direction.</returns>
		public static Quaternion RollScatter(float spreadDegrees, float yawRoll, float pitchRoll)
		{
			if (spreadDegrees <= 0f)
			{
				return Quaternion.identity;
			}

			float x = Mathf.Clamp(yawRoll, -1f, 1f);
			float y = Mathf.Clamp(pitchRoll, -1f, 1f);

			float radius = Mathf.Sqrt(x * x + y * y);
			if (radius > 1f)
			{
				x /= radius;
				y /= radius;
				radius = 1f;
			}

			if (radius <= 0f)
			{
				return Quaternion.identity;
			}

			float deviation = radius * spreadDegrees;

			/* Composed in the aim's own local frame: the deviation is a yaw of the aim direction,
			 * and the roll's direction around the disc becomes a spin about the aim axis. Applied
			 * to the aim direction the result is a point on a cone of exactly `deviation` degrees,
			 * whatever the roll — which is the bound the tests assert. */
			float heading = Mathf.Atan2(y, x) * Mathf.Rad2Deg;

			return Quaternion.AngleAxis(heading, Vector3.forward) *
				   Quaternion.AngleAxis(deviation, Vector3.up);
		}

		/// <summary>
		/// Solves the unit direction to fire along.
		/// </summary>
		/// <remarks>
		/// <see cref="Quaternion.LookRotation(Vector3)"/> normalises internally, so the direction is
		/// deliberately not pre-normalised: the length is what the degeneracy test needs, and
		/// normalising first would discard it.
		/// </remarks>
		/// <param name="origin">The aim origin, from <c>AIController.AimOrigin</c>.</param>
		/// <param name="aimPoint">The world-space point to hit.</param>
		/// <param name="scatter">The held per-cast error rotation, or <see cref="Quaternion.identity"/>.</param>
		/// <param name="direction">Receives the unit aim direction when solvable, else <see cref="Vector3.zero"/>.</param>
		/// <returns>True when a direction was solved. False means the caller should keep its last good rotation.</returns>
		public static bool TryComposeAim(Vector3 origin, Vector3 aimPoint, Quaternion scatter, out Vector3 direction)
		{
			Vector3 toPoint = aimPoint - origin;

			if (toPoint.sqrMagnitude <= MinimumAimDistanceSqr)
			{
				direction = Vector3.zero;
				return false;
			}

			direction = (Quaternion.LookRotation(toPoint) * scatter) * Vector3.forward;
			return true;
		}
	}
}
