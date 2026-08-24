using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Combat sub-state that strafes the NPC in a circle around its current target.
	/// </summary>
	/// <remarks>
	/// Enable <see cref="BaseAIState.KeepsCombatTarget"/> on assets of this type — see
	/// <see cref="GetBehindState"/> for why. <see cref="OrbitDuration"/> bounds the manoeuvre so
	/// the NPC returns to attacking instead of circling indefinitely.
	/// </remarks>
	[CreateAssetMenu(fileName = "New AI Orbit State", menuName = "FishMMO/Character/NPC/AI/Orbit State", order = 0)]
	public class OrbitState : BaseAIState
	{
		/// <summary>
		/// Radius of the orbit around the target.
		/// </summary>
		public float OrbitRadius = 10.0f;
		/// <summary>
		/// Speed at which the AI orbits around the target.
		/// </summary>
		public float OrbitSpeed = 2.0f;
		/// <summary>
		/// Speed at which the AI rotates to face the target.
		/// </summary>
		public float RotationSpeed = 5.0f;

		/// <summary>
		/// Seconds to orbit before returning to the attacking state. 0 orbits until something
		/// else interrupts, which for a combat sub-state means never attacking again.
		/// </summary>
		[Tooltip("Seconds to orbit before rejoining the attack. 0 = orbit indefinitely.")]
		public float OrbitDuration = 2.0f;

		/// <summary>
		/// Called when entering the Orbit state. Initializes orbit angle and checks for target.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Enter(AIController controller)
		{
			if (controller.Target == null)
			{
				Log.Warning("OrbitState", "No target set for OrbitState.");
				controller.TransitionToIdleState(); // Or another default state
				return;
			}

			/* Do NOT reset the orbit angle to zero here.
			 *
			 * NPCGroup.AssignTacticalPositions hands every pack member a distinct angle so a
			 * Surround or Flank tactic spreads them around the target. Zeroing it on entry threw
			 * that assignment away and put every member of the pack on the same point of the
			 * circle — which is the one thing the tactic exists to prevent. Start from wherever
			 * the NPC currently stands relative to its target instead, so an ungrouped NPC still
			 * begins its orbit from a sensible place. */
			controller.OrbitAngle = ResolveStartAngle(controller);
			controller.SubStateTimer = OrbitDuration;
		}

		/// <summary>
		/// Chooses the angle to begin orbiting from.
		/// </summary>
		/// <remarks>
		/// A grouped NPC keeps whatever angle its pack tactic assigned. An ungrouped one starts
		/// from its current bearing to the target, so entering the state does not teleport its
		/// destination to the far side of the circle.
		/// </remarks>
		/// <param name="controller">The AI controller.</param>
		/// <returns>The starting orbit angle in radians.</returns>
		private static float ResolveStartAngle(AIController controller)
		{
			if (controller.Group != null && controller.Group.Tactic != PackTactic.None)
			{
				return controller.OrbitAngle;
			}

			if (controller.Target == null)
			{
				return 0f;
			}

			Vector3 offset = controller.Character.Transform.position - controller.Target.position;
			if (offset.sqrMagnitude < 0.0001f)
			{
				return 0f;
			}

			return Mathf.Atan2(offset.z, offset.x);
		}

		/// <summary>
		/// Called when exiting the Orbit state. Can be used to stop movement or reset parameters.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Exit(AIController controller)
		{
			// Optional: Stop movement or reset parameters if needed
		}

		/// <summary>
		/// Called every frame to update the Orbit state. Handles orbit movement, rotation, and state transitions.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="deltaTime">Frame time.</param>
		public override void UpdateState(AIController controller, float deltaTime)
		{
			// Transition to random movement if requested
			if (controller.RandomizeState)
			{
				controller.TransitionToRandomMovementState();
				return;
			}

			// Transition to idle if target is lost
			if (controller.Target == null)
			{
				controller.TransitionToIdleState(); // Transition if target is lost
				return;
			}

			// Manoeuvre complete — rejoin the fight.
			if (OrbitDuration > 0f)
			{
				controller.SubStateTimer -= deltaTime;
				if (controller.SubStateTimer <= 0f)
				{
					ReturnToCombatOrIdle(controller);
					return;
				}
			}

			// Calculate the new position around the target using polar coordinates.
			// The angle is stored on the controller (per-NPC) to avoid shared SO state.
			controller.OrbitAngle += OrbitSpeed * deltaTime;

			/* A pack tactic sets the ring it wants its members on; honour it so Surround and Kite
			 * produce a ring of the size the group asked for rather than each member using its own
			 * state asset's radius. TacticOrbitRadius had no reader at all before this. */
			float radius = (controller.Group != null && controller.Group.Tactic != PackTactic.None)
				? controller.Group.TacticOrbitRadius
				: OrbitRadius;

			float x = Mathf.Cos(controller.OrbitAngle) * radius;
			float z = Mathf.Sin(controller.OrbitAngle) * radius;
			Vector3 offset = new Vector3(x, 0, z);
			Vector3 targetPosition = controller.Target.position + offset;

			/* Orbiting is cosmetic: if the ring point is off the NavMesh — the target is against a
			 * wall, or on a ledge — abandon the manoeuvre and rejoin the attack rather than
			 * standing still trying to reach a point that does not exist. */
			if (controller.TryMoveTo(targetPosition) == AIMovementResult.Failed)
			{
				ReturnToCombatOrIdle(controller);
				return;
			}

			// Rotate the AI to face the target smoothly
			Vector3 directionToTarget = (controller.Target.position - controller.Character.Transform.position).normalized;
			Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
			controller.Character.Transform.rotation = Quaternion.Slerp(controller.Character.Transform.rotation, targetRotation, RotationSpeed * deltaTime);
		}
	}
}