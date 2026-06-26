using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Sequence (AND) node. Runs each child left-to-right and returns <see cref="AINodeResult.Success"/>
	/// only if every child succeeds. Returns <see cref="AINodeResult.Failure"/> on the first child that fails.
	/// Returns <see cref="AINodeResult.Running"/> if any child is running.
	/// <para>
	/// Use case: "Check health condition AND then transition to retreat state."
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Sequence", menuName = "FishMMO/Character/NPC/AI/Behavior Tree/Sequence")]
	public class AISequence : AICompositeNode
	{
		/// <summary>
		/// Runs each child left-to-right and succeeds only if all children succeed.
		/// </summary>
		/// <param name="controller">The AI controller of the evaluating NPC.</param>
		/// <returns>Success if all children succeed, Failure on the first failing child, Running if any child is running.</returns>
		public override AINodeResult Evaluate(AIController controller)
		{
			if (Children == null) return AINodeResult.Failure;

			for (int i = 0; i < Children.Length; i++)
			{
				if (Children[i] == null) continue;

				AINodeResult result = Children[i].Evaluate(controller);

				if (result == AINodeResult.Failure)
					return AINodeResult.Failure;

				if (result == AINodeResult.Running)
					return AINodeResult.Running;

				// Success → continue to next child
			}

			return AINodeResult.Success;
		}
	}
}