using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Behavior tree leaf that checks if the NPC currently has a combat target.
	/// Returns <see cref="AINodeResult.Success"/> if <see cref="AIController.Target"/> is not null,
	/// <see cref="AINodeResult.Failure"/> otherwise.
	/// <para>
	/// Use case: "If has target → stay in combat" in a behavior tree selector.
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Has Target Node", menuName = "FishMMO/Character/NPC/AI/Behavior Tree/Has Target Node")]
	public class AIHasTargetNode : AIBehaviorNode
	{
		public override AINodeResult Evaluate(AIController controller)
		{
			return controller.Target != null ? AINodeResult.Success : AINodeResult.Failure;
		}
	}
}