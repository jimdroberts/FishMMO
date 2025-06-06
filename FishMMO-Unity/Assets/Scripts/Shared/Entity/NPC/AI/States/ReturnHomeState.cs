using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New AI ReturnHome State", menuName = "FishMMO/Character/NPC/AI/ReturnHome State", order = 0)]
	public class ReturnHomeState : BaseAIState
	{
		public bool CompleteHealOnReturn = true;

		public override void Enter(AIController controller)
		{
			controller.Target = null;
			controller.LookTarget = null;

			controller.Agent.speed = Constants.Character.RunSpeed;

			controller.SetRandomHomeDestination();
			if (controller.Character.TryGet(out ICharacterDamageController characterDamageController))
			{
				//characterDamageController.Immortal = true;
				characterDamageController.CompleteHeal();
			}
		}

		public override void Exit(AIController controller)
		{
			controller.Agent.speed = Constants.Character.WalkSpeed;

			/*if (controller.Character.TryGet(out ICharacterDamageController characterDamageController))
			{
				characterDamageController.Immortal = false;
			}*/
		}

		public override void UpdateState(AIController controller, float deltaTime)
		{
			// Check if the AI has reached its destination
			if (!controller.Agent.pathPending &&
				controller.Agent.remainingDistance < 1.0f)
			{
				// Transition to random movement state
				controller.TransitionToRandomMovementState();
			}
		}
	}
}