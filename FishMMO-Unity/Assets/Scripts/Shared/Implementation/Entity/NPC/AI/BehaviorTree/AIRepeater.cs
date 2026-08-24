using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Decorator that repeats its child a configurable number of times per evaluation.
	/// Returns <see cref="AINodeResult.Success"/> after all repetitions succeed.
	/// Returns <see cref="AINodeResult.Failure"/> if any repetition fails.
	/// <para>
	/// When <see cref="RepeatCount"/> is 0, repeats indefinitely (always returns Running).
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Repeater", menuName = "FishMMO/Character/NPC/AI/Behavior Tree/Repeater")]
	public class AIRepeater : AIBehaviorNode
	{
		/// <summary>
		/// The child node to repeat.
		/// </summary>
		[Tooltip("The child node to repeat.")]
		public AIBehaviorNode Child;

		/// <summary>
		/// Number of times to repeat. 0 = infinite (always Running).
		/// </summary>
		[Tooltip("Number of times to repeat. 0 = infinite (always Running).")]
		public int RepeatCount = 1;

		/// <summary>
		/// Evaluates the child the configured number of times.
		/// </summary>
		/// <param name="controller">The AI controller of the evaluating NPC.</param>
		/// <returns>Success after all repetitions succeed, Failure if any repetition fails, Running for infinite repeat.</returns>
		public override AINodeResult Evaluate(AIController controller)
		{
			if (Child == null) return AINodeResult.Failure;

			if (RepeatCount <= 0)
			{
				// Infinite repeat — run child once and always return Running.
				EvaluateChild(Child, controller);
				return AINodeResult.Running;
			}

			for (int i = 0; i < RepeatCount; i++)
			{
				AINodeResult result = EvaluateChild(Child, controller);

				if (result == AINodeResult.Failure)
					return AINodeResult.Failure;

				if (result == AINodeResult.Running)
					return AINodeResult.Running;
			}

			return AINodeResult.Success;
		}
	}
}