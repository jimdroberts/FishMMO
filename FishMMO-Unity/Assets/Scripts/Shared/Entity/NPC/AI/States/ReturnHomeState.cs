using UnityEngine;

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

			// Set a random home destination for the NPC.
			controller.SetRandomHomeDestination();

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
			// Check if the agent has reached its home destination.
			if (!controller.Agent.pathPending && controller.Agent.remainingDistance < 1.0f)
			{
				// Transition to random movement state after arriving home.
				controller.TransitionToRandomMovementState();
			}
		}
	}
}