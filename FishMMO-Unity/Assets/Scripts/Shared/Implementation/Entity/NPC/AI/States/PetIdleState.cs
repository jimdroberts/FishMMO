using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// A pet's out-of-combat state: heel near its owner, hold position on a Stay order, and enter
	/// combat when the pet's <see cref="PetStance"/> says it should.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Following is driven off <see cref="AIController.Home"/>, which for a pet resolves to its
	/// owner's live position. It is deliberately not driven off <see cref="AIController.Target"/> —
	/// that means "the thing I am fighting" everywhere else in the AI, and the two meanings cannot
	/// share one field. They used to, and the result was a pet that stopped following its owner
	/// permanently the moment a fight ended, because ending a fight clears the target.
	/// </para>
	/// <para>
	/// <b>Not getting stuck</b> is the other half of this state's job. A pet chases a player who
	/// runs through doorways, off ledges and around props, so it meets every NavMesh failure mode
	/// there is. Three layers handle it: a follow band so the pet is not repathing every tick, a
	/// stuck detector that walks it out of geometry, and a distance leash that teleports it when
	/// the owner has simply gone somewhere the pet cannot walk.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New AI Pet Idle State", menuName = "FishMMO/Character/NPC/AI/Pet Idle State", order = 0)]
	public class PetIdleState : BaseAIState
	{
		/// <summary>
		/// How far the pet is allowed to drift from its owner before it moves back.
		/// </summary>
		[Header("Following")]
		[Tooltip("Distance from the owner at which the pet starts catching up.")]
		public float FollowDistance = 3.0f;

		/// <summary>
		/// How far inside <see cref="FollowDistance"/> the pet closes before it stops again.
		/// </summary>
		/// <remarks>
		/// Without a band, a pet sitting exactly at the follow distance flickers between "close
		/// enough, stop" and "too far, move" on alternating ticks, which reads as a stutter and
		/// repaths constantly.
		/// </remarks>
		[Tooltip("Extra distance the pet closes past the follow distance before stopping.")]
		public float FollowHysteresis = 1.0f;

		/// <summary>
		/// If the pet ends up further than this from its owner, it teleports rather than walking.
		/// </summary>
		/// <remarks>
		/// Covers what pathing cannot: the owner mounting and riding away, a teleport, a zone
		/// transition, or a pet left on the wrong side of a chasm.
		/// </remarks>
		[Tooltip("Distance from the owner beyond which the pet teleports instead of walking. 0 disables.")]
		public float TeleportDistance = 40.0f;

		/// <summary>
		/// Seconds the pet may be wedged before it teleports to its owner.
		/// </summary>
		/// <remarks>
		/// The distance leash alone is not enough: a pet jammed behind a crate five metres from its
		/// owner is stuck indefinitely without ever exceeding <see cref="TeleportDistance"/>. This
		/// is the escape hatch for that case, and it only fires after the generic recovery has
		/// already tried to walk the pet out.
		/// </remarks>
		[Tooltip("Seconds wedged on geometry before the pet teleports to its owner. 0 disables.")]
		public float StuckTeleportSeconds = 4.0f;

		/// <summary>
		/// How often (seconds) an aggressive pet sweeps for something to attack.
		/// </summary>
		[Header("Engagement")]
		[Tooltip("Seconds between hostile sweeps while in the Aggressive stance.")]
		public float AggressiveSweepRate = 1.0f;

		/// <summary>
		/// Prepares the pet to heel. Run speed, because it has to be able to catch a sprinting owner.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Enter(AIController controller)
		{
			controller.Agent.speed = Constants.Character.RunSpeed;
			controller.Resume();
			controller.SubStateTimer = 0f;
			controller.PetStuckTimer = 0f;
		}

		/// <summary>
		/// Called when exiting the Pet Idle state.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		public override void Exit(AIController controller)
		{
		}

		/// <summary>
		/// Heels, holds, or engages depending on the pet's orders.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="deltaTime">Seconds since the previous AI tick.</param>
		public override void UpdateState(AIController controller, float deltaTime)
		{
			Pet pet = controller.OwningPet;
			if (pet == null)
			{
				// Not a pet after all — behave like a plain idle state rather than freezing.
				controller.TransitionToRandomMovementState();
				return;
			}

			// An explicit attack order (or a stance-driven engage from a previous tick) left a
			// live target behind; go fight it.
			if (TryEngageExistingTarget(controller))
			{
				return;
			}

			if (TryStanceEngagement(controller, deltaTime))
			{
				return;
			}

			if (pet.MovementOrder == PetMovementOrder.Stay)
			{
				// Holding position: stop, and do not accrue a stuck timer for standing still.
				controller.ClearPath();
				controller.Stop();
				controller.PetStuckTimer = 0f;
				return;
			}

			FollowOwner(controller, pet, deltaTime);
		}

		/// <summary>
		/// Switches to the attacking state when the pet already has a valid live target.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <returns>True if the pet entered combat.</returns>
		private static bool TryEngageExistingTarget(AIController controller)
		{
			if (controller.Target == null || controller.AttackingState == null)
			{
				return false;
			}

			ICharacter target = controller.TargetCharacter;
			if (!AITargetSelection.IsValidTarget(target))
			{
				controller.Target = null;
				controller.LookTarget = null;
				return false;
			}

			controller.ChangeState(controller.AttackingState);
			return true;
		}

		/// <summary>
		/// Applies the pet's stance: an aggressive pet hunts; defensive and passive pets do not
		/// start anything here (defensive entry is event-driven, from the owner being attacked).
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="deltaTime">Seconds since the previous AI tick.</param>
		/// <returns>True if the pet entered combat.</returns>
		private bool TryStanceEngagement(AIController controller, float deltaTime)
		{
			if (controller.AttackingState == null || !controller.PetStanceAllowsAutoEngage(true))
			{
				return false;
			}

			controller.SubStateTimer -= deltaTime;
			if (controller.SubStateTimer > 0f)
			{
				return false;
			}
			controller.SubStateTimer = AggressiveSweepRate;

			List<ICharacter> candidates = controller.CombatTargetBuffer;
			candidates.Clear();

			// Sweep with the attacking state's own detection settings so an aggressive pet's
			// engagement range matches the range it will actually fight at.
			if (!controller.AttackingState.SweepForEnemies(controller, candidates))
			{
				return false;
			}

			controller.ChangeState(controller.AttackingState, candidates);
			return true;
		}

		/// <summary>
		/// Keeps the pet within its follow band of its owner, recovering when it gets wedged.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="pet">The pet.</param>
		/// <param name="deltaTime">Seconds since the previous AI tick.</param>
		private void FollowOwner(AIController controller, Pet pet, float deltaTime)
		{
			if (pet.PetOwner == null || pet.PetOwner.Transform == null)
			{
				// Orphaned: the owner despawned. Stand still rather than pathing to a stale point.
				controller.ClearPath();
				controller.Stop();
				return;
			}

			Vector3 ownerPosition = pet.PetOwner.Transform.position;
			Vector3 petPosition = controller.Character.Transform.position;
			float sqrDistance = (ownerPosition - petPosition).sqrMagnitude;

			// --- Hard leash: too far away to be worth walking back. ---
			if (TeleportDistance > 0f && sqrDistance > TeleportDistance * TeleportDistance)
			{
				TeleportToOwner(controller, ownerPosition);
				return;
			}

			float followDistance = Mathf.Max(FollowDistance, controller.Agent.radius * 2f);

			// --- Close enough: stand down. ---
			if (sqrDistance <= followDistance * followDistance)
			{
				controller.ClearPath();
				controller.Stop();
				controller.PetStuckTimer = 0f;
				return;
			}

			// --- Catching up. ---
			controller.Resume();

			/* Aim for a point on a sphere around the owner rather than at their feet, so the pet
			 * settles beside them instead of shoving into their collider. Closing slightly inside
			 * the follow distance is the hysteresis that stops the stop/go flicker. */
			float standoff = Mathf.Max(followDistance - FollowHysteresis, controller.Agent.radius * 1.5f);
			Vector3 heel = Vector3Extensions.GetNearestPositionOnSphere(petPosition, ownerPosition, standoff);

			AIMovementResult result = controller.TryMoveTo(heel);

			if (result == AIMovementResult.Failed)
			{
				// Nowhere near the owner is on the NavMesh at all.
				controller.PetStuckTimer += deltaTime;
			}
			else
			{
				AIMovementProgress progress = controller.GetMovementProgress(deltaTime, followDistance);

				/* A partial path means the owner is somewhere the pet cannot walk to — across a
				 * gap, up a ledge, behind a closed door. Walking to the nearest reachable point
				 * and standing there is the classic "my pet is stuck on the terrain" report, so
				 * count it as stuck from the outset rather than waiting to notice it stopped. */
				if (progress == AIMovementProgress.Stuck || controller.LastPathWasPartial)
				{
					controller.PetStuckTimer += deltaTime;

					// Try to walk out of it first; teleporting is visible, so it comes last.
					controller.TryRecoverFromStuck(ownerPosition);
				}
				else if (progress != AIMovementProgress.Computing)
				{
					controller.PetStuckTimer = 0f;
				}
			}

			// --- Soft leash: wedged long enough that walking is not going to resolve it. ---
			if (StuckTeleportSeconds > 0f && controller.PetStuckTimer >= StuckTeleportSeconds)
			{
				TeleportToOwner(controller, ownerPosition);
			}
		}

		/// <summary>
		/// Places the pet beside its owner and clears its navigation state.
		/// </summary>
		/// <param name="controller">The AI controller.</param>
		/// <param name="ownerPosition">The owner's world position.</param>
		private void TeleportToOwner(AIController controller, Vector3 ownerPosition)
		{
			// Beside the owner, not inside them.
			Vector3 arrival = Vector3Extensions.GetNearestPositionOnSphere(
				controller.Character.Transform.position,
				ownerPosition,
				Mathf.Max(FollowDistance * 0.5f, controller.Agent.radius * 2f));

			controller.WarpTo(arrival);
			controller.Stop();
			controller.PetStuckTimer = 0f;
		}
	}
}
