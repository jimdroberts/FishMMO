using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Behavior tree leaf that checks if the NPC belongs to an <see cref="NPCGroup"/>
	/// and whether the group is currently in combat.
	/// Returns <see cref="AINodeResult.Success"/> when the group is fighting.
	/// <para>
	/// Use case: "If my pack is in combat → join the fight (even if I haven't been attacked)."
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Group In Combat Node", menuName = "FishMMO/Character/NPC/AI/Behavior Tree/Group In Combat Node")]
	public class AIGroupInCombatNode : AIBehaviorNode
	{
		public override AINodeResult Evaluate(AIController controller)
		{
			if (controller.Group == null)
				return AINodeResult.Failure;

			return controller.Group.IsInCombat ? AINodeResult.Success : AINodeResult.Failure;
		}
	}
}