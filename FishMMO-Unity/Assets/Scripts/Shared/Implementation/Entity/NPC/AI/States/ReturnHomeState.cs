using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// AI State for returning the NPC to its home position. Handles healing and movement speed adjustments.
	/// </summary>
	[CreateAssetMenu(fileName = "New AI ReturnHome State", menuName = "FishMMO/Character/NPC/AI/ReturnHome State", order = 0)]
	public class ReturnHomeState : BaseAIState
	{
		/// <summary>
		/// If true, the NPC will be fully healed upon returning home.
		/// </summary>
		public bool CompleteHealOnReturn = true;

		/// <summary>
		/// How close to home counts as home. Also the fallback scatter radius when home itself
		/// cannot be sampled onto the NavMesh.
		/// </summary>
		[Tooltip("Distance from home that counts as arriving.")]
		public float HomeArrivalRadius = 2.0f;

		/// <summary>
		/// Called when the state is entered. Sets the NPC's destination to home, increases speed, and heals if applicable.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		public override void Enter(AIController controller)
		{
			// Clear any combat targets and look targets.
			controller.Target = null;
			controller.LookTarget = null;

			// Set agent speed to run speed for quick return.
			controller.Agent.speed = Constants.Character.RunSpeed;

			controller.Resume();

			/* Head for home itself rather than a random point near it. The random offset exists so
			 * a pack does not stack on one pixel, but it is applied around the *destination*, and
			 * sampling it can fail — which used to leave the NPC with no path while every arrival
			 * check reported it had already arrived. HomeArrivalRadius reintroduces the spread
			 * without risking that. */
			if (controller.TryMoveTo(controller.Home, throttle: false) == AIMovementResult.Failed &&
				HomeArrivalRadius > 0f)
			{
				controller.SetRandomHomeDestination(HomeArrivalRadius);
			}

			// Heal the NPC if CompleteHealOnReturn is true and a damage controller is present.
			if (controller.Character.TryGet(out ICharacterDamageController characterDamageController))
			{
				// Optionally, the NPC could be made immortal while returning home.
				// characterDamageController.Immortal = true;
				characterDamageController.CompleteHeal();
			}
		}

		/// <summary>
		/// Called when the state is exited. Resets movement speed and optionally disables immortality.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		public override void Exit(AIController controller)
		{
			// Reset agent speed to walk speed after returning home.
			controller.Agent.speed = Constants.Character.WalkSpeed;

			// Optionally, disable immortality when leaving this state (commented out).
			/*if (controller.Character.TryGet(out ICharacterDamageController characterDamageController))
			{
				characterDamageController.Immortal = false;
			}*/
		}

		/// <summary>
		/// Called every frame while in this state. Checks if the NPC has reached its home destination and transitions to random movement.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		/// <param name="deltaTime">Time since last update.</param>
		public override void UpdateState(AIController controller, float deltaTime)
		{
			/* A pet's Home is its owner, so this state doubles as "catch up with the player".
			 * Repathing every tick keeps it tracking a moving anchor; for a stationary NPC home
			 * the throttle makes it a cheap no-op. */
			if (controller.OwningPet != null)
			{
				controller.TryMoveTo(controller.Home);
			}

			switch (controller.GetMovementProgress(deltaTime, HomeArrivalRadius))
			{
				case AIMovementProgress.Arrived:
					controller.TransitionToRandomMovementState();
					return;

				case AIMovementProgress.Stuck:
					/* Returning home is the one movement an NPC must not fail: it is what pulls a
					 * leashed mob out of terrain it should never have been in. Recovery escalates
					 * to a warp, with home as the fallback. */
					controller.TryRecoverFromStuck(controller.Home);
					return;

				case AIMovementProgress.Idle:
					// No path at all — re-issue, unthrottled.
					if (controller.TryMoveTo(controller.Home, throttle: false) == AIMovementResult.Failed)
					{
						// Home is not on the NavMesh. Warping is the only way back.
						controller.WarpTo(controller.Home);
						controller.TransitionToRandomMovementState();
					}
					return;

				default:
					return;
			}
		}
	}
}