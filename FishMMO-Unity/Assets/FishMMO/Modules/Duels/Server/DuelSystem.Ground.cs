using KinematicCharacterController;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Finding the duel ground: level, standable starts for both players, a box that holds them.
	/// </summary>
	/// <remarks>
	/// Every query runs against the challenger's motor's own <see cref="KinematicCharacterMotor.PhysicsScene"/>, with its
	/// own layers and capsule (as <see cref="StandingSpot"/>): scene stacking loads several copies of a scene at once, and
	/// "standable" must mean what it means to the character controller.
	/// </remarks>
	public partial class DuelSystem
	{
		/// <summary>How far above the ground's middle the search for each start's footing begins.</summary>
		private const float FootingSearchAbove = 3f;

		/// <summary>Raised off the footing so the capsule does not start inside it.</summary>
		private const float FootingLift = 0.05f;

		private static readonly Collider[] overlapBuffer = new Collider[8];

		/// <summary>
		/// The ground for a duel between two players, and where each starts: centred between them and running from one to
		/// the other when that ground is level enough, else turned or moved (onto either player) until it is. False when
		/// nothing nearby will do.
		/// </summary>
		private bool TryPlaceArena(IPlayerCharacter challenger, IPlayerCharacter target, out DuelArena arena, out Vector3 challengerStart, out Vector3 targetStart)
		{
			arena = default;
			challengerStart = targetStart = default;
			KinematicCharacterMotor motor = challenger.Motor;
			if (motor == null || challenger.Transform == null || target.Transform == null)
			{
				return false;
			}

			Vector3 size = DuelRules.ClampSize(arenaSize);
			Vector3 a = challenger.Transform.position;
			Vector3 b = target.Transform.position;
			float bearing = DuelRules.Bearing(a, b);
			float capsuleHeight = motor.Capsule != null ? motor.Capsule.height : 2f;
			float standing = capsuleHeight + StandingSpot.MinimumClearanceMargin;
			Vector3[] centres = { (a + b) * 0.5f, a, b };

			for (int c = 0; c < centres.Length; ++c)
			{
				for (int y = 0; y < DuelRules.YawOffsets.Length; ++y)
				{
					float yaw = bearing + DuelRules.YawOffsets[y];
					DuelRules.Starts(centres[c], size, yaw, out Vector3 first, out Vector3 second);
					if (!TryFindFooting(motor, first, size, DuelRules.FirstFacing(yaw), out Vector3 firstFooting) ||
						!TryFindFooting(motor, second, size, DuelRules.SecondFacing(yaw), out Vector3 secondFooting) ||
						!DuelRules.TryBuildArena(firstFooting, secondFooting, size, yaw, standing, out arena))
					{
						continue;
					}

					/* The challenger takes the start nearer them, the target the other, so nobody is carried past their
					 * opponent. The second start faces the first; swapping who stands where swaps the ground's facing too. */
					if ((firstFooting - a).sqrMagnitude <= (secondFooting - a).sqrMagnitude)
					{
						challengerStart = firstFooting;
						targetStart = secondFooting;
					}
					else
					{
						arena.Yaw += 180f;
						challengerStart = secondFooting;
						targetStart = firstFooting;
					}
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Standable footing below <paramref name="start"/>: the first stable ground the motor stands on within the
		/// ground's height, gentle enough to stand on, with room for the capsule.
		/// </summary>
		private static bool TryFindFooting(KinematicCharacterMotor motor, Vector3 start, Vector3 size, Quaternion facing, out Vector3 footing)
		{
			footing = default;
			float above = Mathf.Min(FootingSearchAbove, size.y * 0.5f);
			Vector3 origin = start + Vector3.up * above;
			if (!motor.PhysicsScene.Raycast(origin, Vector3.down, out RaycastHit hit, size.y, motor.StableGroundLayers, QueryTriggerInteraction.Ignore))
			{
				return false;
			}
			if (Vector3.Angle(Vector3.up, hit.normal) > motor.MaxStableSlopeAngle)
			{
				return false;
			}
			Vector3 candidate = hit.point + Vector3.up * FootingLift;
			if (motor.CharacterCollisionsOverlap(candidate, facing, overlapBuffer) > 0)
			{
				return false;
			}
			footing = candidate;
			return true;
		}
	}
}
