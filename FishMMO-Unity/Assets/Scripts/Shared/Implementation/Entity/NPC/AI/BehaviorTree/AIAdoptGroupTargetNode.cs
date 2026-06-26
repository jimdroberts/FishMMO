using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Behavior tree leaf that adopts the group's shared target. If the NPC belongs to
	/// an <see cref="NPCGroup"/> with a <see cref="NPCGroup.GroupTarget"/>, this node
	/// sets the NPC's <see cref="AIController.Target"/> to match and returns Success.
	/// <para>
	/// Use case: "Focus the tank's target" → place this before a StateTransition to AttackState.
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Adopt Group Target Node", menuName = "FishMMO/Character/NPC/AI/Behavior Tree/Adopt Group Target Node")]
	public class AIAdoptGroupTargetNode : AIBehaviorNode
	{
		/// <summary>
		/// Adopts the group's target as this NPC's target. Returns Success if the group
		/// has a target and it was adopted, Failure otherwise.
		/// </summary>
		/// <param name="controller">The AI controller of the evaluating NPC.</param>
		/// <returns>Success if the group target was adopted, Failure if no group or no group target.</returns>
		public override AINodeResult Evaluate(AIController controller)
		{
			if (controller.Group == null)
				return AINodeResult.Failure;

			Transform groupTarget = controller.Group.GroupTarget;
			if (groupTarget == null)
				return AINodeResult.Failure;

			controller.Target = groupTarget;
			controller.LookTarget = groupTarget;
			return AINodeResult.Success;
		}
	}
}