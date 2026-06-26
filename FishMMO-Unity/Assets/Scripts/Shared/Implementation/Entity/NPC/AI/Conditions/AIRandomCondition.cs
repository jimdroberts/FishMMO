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
		/// Uses the NPC's seeded RNG for deterministic behaviour.
		/// </summary>
		/// <param name="controller">The AI controller of the NPC.</param>
		/// <param name="self">The NPC's character.</param>
		/// <param name="target">The NPC's current target (may be null).</param>
		/// <returns>True with probability Chance, false otherwise.</returns>
		public override bool Evaluate(AIController controller, ICharacter self, ICharacter target)
		{
			DeterministicRNG rng = controller.NpcRNG;
			float roll = (rng ?? DeterministicRNG.Shared).NextFloat();
			return roll <= Chance;
		}
	}
}
