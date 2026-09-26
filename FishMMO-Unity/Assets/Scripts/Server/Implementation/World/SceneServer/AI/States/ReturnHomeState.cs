using UnityEngine;
using FishMMO.Shared.Core;
using FishMMO.Shared;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer.AI
{
	/// <summary>
	/// AI State for returning the NPC to its home position. Handles healing and movement speed adjustments.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Two uses.</b> The leash sends a fighting NPC home through this state, and it is also one of
	/// the calm movement states <see cref="AIController.TransitionToRandomMovementState"/> drifts
	/// between. Only the first is an evade: while a leash that ended a fight has the NPC walking
	/// home it takes no damage, threat, taunt, debuff or knockback
	/// (<see cref="AIController.IsEvading"/>, <see cref="CharacterEvade"/>). The evade is
	/// held by the controller, not by this shared asset, and ends by construction the moment this
	/// state stops being the NPC's current state — there is nothing here to switch off.
	/// </para>
	/// <para>
	/// <b>The heal belongs to the leash, too.</b> <see cref="CompleteHealOnReturn"/> is authored
	/// here but applied by <see cref="AIController"/>'s leash check, which is the only thing that
	/// knows a leash — not a calm pick — sent the NPC home. It used to be applied by
	/// <see cref="Enter"/>, and since this state is also in the calm movement pool, a damaged NPC
	/// that simply drifted home between fights was topped up to full on the way.
	/// </para>
	/// </remarks>
	[CreateAssetMenu(fileName = "New AI ReturnHome State", menuName = "FishMMO/Character/NPC/AI/ReturnHome State", order = 0)]
	public class ReturnHomeState : BaseAIState
	{
		/// <summary>
		/// If true, the NPC is fully healed when a LEASH sends it home through this state. A calm
		/// stroll home picked from the movement pool never heals. Applied by
		/// <see cref="AIController"/> (see the class remarks).
		/// </summary>
		[Tooltip("Fully heal the NPC when a leash sends it home. A calm stroll home never heals.")]
		public bool CompleteHealOnReturn = true;

		/// <summary>
		/// How close to home counts as home. Also the fallback scatter radius when home itself
		/// cannot be sampled onto the NavMesh.
		/// </summary>
		[Tooltip("Distance from home that counts as arriving.")]
		public float HomeArrivalRadius = 2.0f;

		/// <summary>
		/// Called when the state is entered. Sets the NPC's destination to home and increases speed.
		/// Does not heal: see the class remarks.
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

			/* No heal here: whether to heal depends on WHY the NPC is going home, which only the
			 * leash knows — see the class remarks and AIController.CheckLeash.
			 *
			 * No Immortal either: the evade lives on the controller (see the class remarks), because
			 * Immortal belongs to the prefab, the corpse path and administrators, and borrowing it
			 * would have to be undone on every way out of this state. */
		}

		/// <summary>
		/// Called when the state is exited. Resets movement speed.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		public override void Exit(AIController controller)
		{
			// Reset agent speed to walk speed after returning home.
			controller.Agent.speed = Constants.Character.WalkSpeed;
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
					// Home: the evade ends, the table is emptied, and the NPC idles again.
					controller.CompleteReturnHome();
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
						controller.CompleteReturnHome();
					}
					return;

				default:
					return;
			}
		}
	}
}