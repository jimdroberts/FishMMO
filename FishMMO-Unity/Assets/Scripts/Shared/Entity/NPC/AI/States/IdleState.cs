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

		public override void UpdateState(AIController controller)
		{
			// Check for nearby enemies
			if (SweepForEnemies(controller, out List<ICharacter> enemies))
			{
				controller.ChangeState(controller.AttackingState, enemies);
				return;
			}
		}
	}
}