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
			if (controller.Character.TryGet(out ICharacterDamageController characterDamageController))
			{
				characterDamageController.Immortal = false;
			}
			controller.TransitionToRandomMovementState();
		}

		public override void Update(AIController controller)
		{

		}
	}
}