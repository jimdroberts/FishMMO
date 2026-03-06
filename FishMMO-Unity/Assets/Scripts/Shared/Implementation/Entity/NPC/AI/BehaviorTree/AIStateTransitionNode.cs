using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Leaf node that transitions the NPC's state machine to a specific <see cref="BaseAIState"/>.
	/// Always returns <see cref="AINodeResult.Success"/> after triggering the transition.
	/// <para>
	/// This is the bridge between the behavior tree (decision layer) and the state machine
	/// (execution layer). The tree decides "attack", and this node calls
	/// <see cref="AIController.ChangeState"/> to make it happen.
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New AI State Transition", menuName = "FishMMO/Character/NPC/AI/Behavior Tree/State Transition")]
	public class AIStateTransitionNode : AIBehaviorNode
	{
		[Tooltip("The AI state to transition to when this node executes.")]
		public BaseAIState TargetState;

		public override AINodeResult Evaluate(AIController controller)
		{
			if (TargetState == null)
				return AINodeResult.Failure;

			// Only transition if not already in the target state to avoid re-entry spam.
			if (controller.CurrentState != TargetState)
			{
				controller.ChangeState(TargetState);
			}

			return AINodeResult.Success;
		}
	}
}