using UnityEngine;
using KinematicCharacterController;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Whether a player is standing somewhere it can safely be put back down later: the rule a bind point and a Mark
	/// (issue #241) are both recorded under, because both are arrived at later with no further validation.
	/// </summary>
	/// <remarks>Moved out of <c>BindstoneAction</c> so the bindstone and the travel spells share one rule.</remarks>
	public static class StandingSpot
	{
		/// <summary>
		/// Free space a character needs above its own head to be able to move after arriving.
		/// </summary>
		/// <remarks>
		/// One metre. Enough that an arrival cannot land somebody in a gap they exactly fill — which passes an overlap
		/// test and is still stuck — while staying under any sane indoor ceiling, so a spot inside a building is not
		/// refused for being inside a building.
		/// </remarks>
		public const float MinimumClearanceMargin = 1.0f;

		/// <summary>The default total clearance asked for, in metres from the ground.</summary>
		public const float DefaultRequiredClearance = 3.0f;

		/// <summary>Buffer for the capsule overlap test. Static: each test is consumed inside one synchronous call.</summary>
		private static readonly Collider[] overlapBuffer = new Collider[8];

		/// <summary>Buffer for the headroom sweep.</summary>
		private static readonly RaycastHit[] sweepBuffer = new RaycastHit[8];

		/// <summary>
		/// Returns true when the player is standing somewhere they can safely be put back down.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Every query runs against the motor's own <see cref="KinematicCharacterMotor.PhysicsScene"/>
		/// and its own capsule dimensions, rather than the global <c>Physics</c> API. Both matter:
		/// scene stacking loads several copies of a scene at once, so a global query would happily
		/// answer using another channel's geometry, and the character's real capsule is what has to
		/// fit — a point test would pass in a gap the player cannot occupy.
		/// </para>
		/// <para>
		/// The layer masks come from the motor too (<c>CollidableLayers</c>, <c>StableGroundLayers</c>),
		/// so "solid" here means exactly what it means to the character controller. A second,
		/// hand-maintained mask here would be one more thing to drift.
		/// </para>
		/// </remarks>
		/// <param name="player">The player standing there.</param>
		/// <param name="requiredClearanceSetting">Total vertical clearance asked for, in metres from the ground; floored at the capsule height plus <see cref="MinimumClearanceMargin"/>.</param>
		/// <param name="rejection">Receives why the position was refused.</param>
		/// <returns>True when the position is safe to record.</returns>
		public static bool IsValid(IPlayerCharacter player, float requiredClearanceSetting, out string rejection)
		{
			rejection = null;

			KinematicCharacterMotor motor = player.Motor;
			if (motor == null)
			{
				rejection = "no character motor";
				return false;
			}

			/* Ask the controller, rather than re-deriving grounding from a raycast. IsStableOnGround
			 * already means "touching ground the character may stand on", slope limit included, and
			 * it is the same answer the movement code acts on — so a bind can never be accepted on
			 * a surface the player would immediately slide off. FoundAnyGround is deliberately not
			 * enough: that is true on a 70-degree face the character is sliding down. */
			if (!motor.GroundingStatus.IsStableOnGround)
			{
				rejection = "not standing on stable ground";
				return false;
			}

			Vector3 position = motor.Transform.position;
			Quaternion rotation = motor.Transform.rotation;

			/* Inside geometry. The controller de-collides itself as it moves, so an overlap here
			 * means the character is somewhere it could not have walked — clipped into terrain, or
			 * pushed inside a wall by a knockback. Recording that position would respawn them
			 * inside it, with no movement input able to get them out. */
			if (motor.CharacterCollisionsOverlap(position, rotation, overlapBuffer) > 0)
			{
				rejection = "overlapping world geometry";
				return false;
			}

			/* Clearance, and with it the under-the-map case. A player who has clipped through the
			 * world stands on the underside of the terrain perfectly stably and overlaps nothing,
			 * so neither check above sees it — but there is world directly above their head, which
			 * there is not when standing on the world the right way up.
			 *
			 * Swept with the character's own capsule rather than raycast from a point, so a gap the
			 * player's shoulders would not fit through does not read as open sky.
			 *
			 * The sweep measures travel available above the head, and the capsule already occupies
			 * its own height — so the distance asked for is the total clearance minus that height,
			 * not the total. Sweeping the full figure would demand a room more than twice the
			 * player's height and refuse every honest indoor bind. */
			float capsuleHeight = motor.Capsule != null ? motor.Capsule.height : 0f;
			float requiredClearance = Mathf.Max(requiredClearanceSetting, capsuleHeight + MinimumClearanceMargin);
			float requiredHeadroom = Mathf.Max(requiredClearance - capsuleHeight, MinimumClearanceMargin);

			int hits = motor.CharacterCollisionsSweep(
				position,
				rotation,
				motor.CharacterUp,
				requiredHeadroom,
				out RaycastHit closestHit,
				sweepBuffer);

			if (hits > 0 && closestHit.collider != null)
			{
				rejection = $"needs {requiredClearance:0.00}m of clearance but only has " +
							$"{capsuleHeight + closestHit.distance:0.00}m below '{closestHit.collider.name}'";
				return false;
			}

			return true;
		}
	}
}
