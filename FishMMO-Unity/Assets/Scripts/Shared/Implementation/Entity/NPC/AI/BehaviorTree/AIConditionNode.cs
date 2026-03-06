using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Leaf node that evaluates an <see cref="AIAbilityCondition"/> and returns
	/// <see cref="AINodeResult.Success"/> or <see cref="AINodeResult.Failure"/>.
	/// <para>
	/// Use case: "Is the NPC's health below 30%?"
	/// </para>
	/// <para>
	/// The existing <see cref="AIAbilityCondition"/> system is reused so designers
	/// don't need to create duplicate condition assets. Any condition that works in
	/// an ability rotation also works as a behavior tree guard.
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Condition Node", menuName = "FishMMO/Character/NPC/AI/Behavior Tree/Condition Node")]
	public class AIConditionNode : AIBehaviorNode
	{
		[Tooltip("The condition to evaluate. Uses the same condition assets as ability rotations.")]
		public AIAbilityCondition Condition;

		public override AINodeResult Evaluate(AIController controller)
		{
			if (Condition == null)
				return AINodeResult.Failure;

			// Resolve the current target character (may be null if not in combat).
			ICharacter target = null;
			if (controller.Target != null)
			{
				target = controller.Target.GetComponent<ICharacter>();
			}

			bool result = Condition.Evaluate(controller, controller.Character, target);
			return result ? AINodeResult.Success : AINodeResult.Failure;
		}
	}
}