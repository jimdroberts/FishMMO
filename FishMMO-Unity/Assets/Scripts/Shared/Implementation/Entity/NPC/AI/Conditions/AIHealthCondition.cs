using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that evaluates based on a character's health percentage.
	/// Can check either the NPC's own health or the current target's health.
	/// <para>
	/// Examples:<br/>
	/// - "Self health ≤ 40%" → use a defensive or healing ability.<br/>
	/// - "Target health ≤ 20%" → use an execute ability.<br/>
	/// - "Self health ≥ 80%" → use an offensive stance ability.
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Health Condition", menuName = "FishMMO/Character/NPC/AI/Conditions/Health Condition")]
	public class AIHealthCondition : AIAbilityCondition
	{
		/// <summary>
		/// Which character's health to evaluate.
		/// </summary>
		[Tooltip("Which character's health to evaluate.")]
		public ConditionSubject Subject = ConditionSubject.Self;

		/// <summary>
		/// The comparison operator to use against the health percentage.
		/// </summary>
		[Tooltip("How to compare the health percentage.")]
		public ComparisonOperator Operator = ComparisonOperator.LessOrEqual;

		/// <summary>
		/// Health percentage threshold (0–1). E.g., 0.4 = 40%.
		/// </summary>
		[Range(0f, 1f)]
		[Tooltip("Health percentage threshold (0 = 0%, 1 = 100%).")]
		public float Threshold = 0.5f;

		/// <summary>
		/// Evaluates whether the subject's health percentage satisfies the comparison.
		/// Returns false if the subject is null, dead, or has no health resource.
		/// </summary>
		public override bool Evaluate(AIController controller, ICharacter self, ICharacter target)
		{
			ICharacter subject = Subject == ConditionSubject.Self ? self : target;
			if (subject == null)
				return false;

			if (!subject.TryGet(out ICharacterDamageController dmg) || !dmg.IsAlive)
				return false;

			CharacterResourceAttribute health = dmg.ResourceInstance;
			if (health == null || health.FinalValue <= 0f)
				return false;

			float healthPct = health.CurrentValue / health.FinalValue;
			return Compare(healthPct, Operator, Threshold);
		}
	}
}
