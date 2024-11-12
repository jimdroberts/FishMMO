using System.Collections.Generic;

namespace FishMMO.Shared
{
	public class IdleState : BaseAIState
	{
		public override void Enter(AIController controller)
		{
		}

		public override void Exit(AIController controller)
		{
		}

		public override void UpdateState(AIController controller, float deltaTime)
		{
			// Check for nearby enemies
			if (controller.AttackingState != null &&
				SweepForEnemies(controller, out List<ICharacter> enemies))
			{
				controller.ChangeState(controller.AttackingState, enemies);
				return;
			}
		}
	}
}