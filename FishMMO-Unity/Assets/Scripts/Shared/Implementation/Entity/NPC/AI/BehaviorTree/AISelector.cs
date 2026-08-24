using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selector (OR) node. Tries each child left-to-right and returns <see cref="AINodeResult.Success"/>
	/// as soon as one child succeeds. Returns <see cref="AINodeResult.Failure"/> only if every child fails.
	/// Returns <see cref="AINodeResult.Running"/> if any child is running.
	/// <para>
	/// Use case: "Try heal → if that fails, try attack → if that fails, wander."
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Selector", menuName = "FishMMO/Character/NPC/AI/Behavior Tree/Selector")]
	public class AISelector : AICompositeNode
	{
		/// <summary>
		/// Tries each child left-to-right and returns Success on the first successful child.
		/// </summary>
		/// <param name="controller">The AI controller of the evaluating NPC.</param>
		/// <returns>Success if any child succeeds, Failure if all children fail, Running if any child is running.</returns>
		public override AINodeResult Evaluate(AIController controller)
		{
			if (Children == null) return AINodeResult.Failure;

			for (int i = 0; i < Children.Length; i++)
			{
				if (Children[i] == null) continue;

				AINodeResult result = EvaluateChild(Children[i], controller);

				if (result == AINodeResult.Success)
					return AINodeResult.Success;

				if (result == AINodeResult.Running)
					return AINodeResult.Running;

				// Failure → try next child
			}

			return AINodeResult.Failure;
		}
	}
}