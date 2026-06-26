using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Decorator that inverts the child's result.
	/// <see cref="AINodeResult.Success"/> becomes <see cref="AINodeResult.Failure"/> and vice versa.
	/// <see cref="AINodeResult.Running"/> passes through unchanged.
	/// <para>
	/// Use case: "If NOT low health → continue attacking."
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Inverter", menuName = "FishMMO/Character/NPC/AI/Behavior Tree/Inverter")]
	public class AIInverter : AIBehaviorNode
	{
		/// <summary>
		/// The child node whose result will be inverted.
		/// </summary>
		[Tooltip("The child node whose result will be inverted.")]
		public AIBehaviorNode Child;

		/// <summary>
		/// Evaluates the child node and inverts its result.
		/// </summary>
		/// <param name="controller">The AI controller of the evaluating NPC.</param>
		/// <returns>Failure if the child succeeded, Success if the child failed, Running if the child is running.</returns>
		public override AINodeResult Evaluate(AIController controller)
		{
			if (Child == null) return AINodeResult.Failure;

			AINodeResult result = Child.Evaluate(controller);

			switch (result)
			{
				case AINodeResult.Success: return AINodeResult.Failure;
				case AINodeResult.Failure: return AINodeResult.Success;
				default: return result;
			}
		}
	}
}