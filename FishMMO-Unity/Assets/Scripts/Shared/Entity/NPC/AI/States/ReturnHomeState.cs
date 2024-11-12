namespace FishMMO.Shared
{
	public class ReturnHomeState : BaseAIState
	{
		public bool CompleteHealOnReturn = true;

		public override void Enter(AIController controller)
		{
			controller.SetRandomHomeDestination();
			if (controller.Character.TryGet(out ICharacterDamageController characterDamageController))
			{
				characterDamageController.Immortal = true;
				characterDamageController.CompleteHeal();
			}
		}

		public override void Exit(AIController controller)
		{
		}

		public override void UpdateState(AIController controller, float deltaTime)
		{
			// Check if the AI has reached its destination
			if (!controller.Agent.pathPending &&
				controller.Agent.remainingDistance < 1.0f)
			{
				if (controller.Character.TryGet(out ICharacterDamageController characterDamageController))
				{
					characterDamageController.Immortal = false;
				}

				// Transition to random movement state
				controller.TransitionToRandomMovementState();
			}
		}
	}
}