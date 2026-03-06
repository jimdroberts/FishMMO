using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that passes with a configurable random probability each evaluation.
	/// Useful for introducing variety — e.g., "30% chance to use a special attack".
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Random Condition", menuName = "FishMMO/Character/NPC/AI/Conditions/Random Condition")]
	public class AIRandomCondition : AIAbilityCondition
	{
		/// <summary>
		/// Probability (0–1) that the condition evaluates to true.
		/// 0 = never, 1 = always.
		/// </summary>
		[Range(0f, 1f)]
		[Tooltip("Probability (0-1) that this condition is true each evaluation.")]
		public float Chance = 0.5f;

		/// <summary>
		/// Returns true with probability <see cref="Chance"/>.
		/// </summary>
		public override bool Evaluate(AIController controller, ICharacter self, ICharacter target)
		{
			System.Random rng = controller.NpcRNG;
			float roll = rng != null ? (float)rng.NextDouble() : Random.value;
			return roll <= Chance;
		}
	}
}
