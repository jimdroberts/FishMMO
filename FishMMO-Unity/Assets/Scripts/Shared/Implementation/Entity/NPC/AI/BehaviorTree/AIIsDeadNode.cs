using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Behavior tree leaf that checks if the NPC is alive.
	/// Returns <see cref="AINodeResult.Success"/> when alive,
	/// <see cref="AINodeResult.Failure"/> when dead or missing damage controller.
	/// <para>
	/// Use case: First check in a selector — "If dead → DeadState".
	/// Combine with <see cref="AIInverter"/> to get "IsAlive".
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Is Dead Node", menuName = "FishMMO/Character/NPC/AI/Behavior Tree/Is Dead Node")]
	public class AIIsDeadNode : AIBehaviorNode
	{
		public override AINodeResult Evaluate(AIController controller)
		{
			if (controller.Character == null)
				return AINodeResult.Failure;

			if (controller.Character.TryGet(out ICharacterDamageController dmg))
			{
				return dmg.IsAlive ? AINodeResult.Failure : AINodeResult.Success;
			}

			return AINodeResult.Failure;
		}
	}
}