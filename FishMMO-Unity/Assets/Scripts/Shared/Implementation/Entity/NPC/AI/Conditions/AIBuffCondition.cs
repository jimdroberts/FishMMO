using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Condition that checks whether a character has (or doesn't have) a specific buff or debuff.
	/// <para>
	/// Examples:<br/>
	/// - "Self has buff 42" → target already has a HoT, skip re-applying.<br/>
	/// - "Target missing debuff 7" → apply the DoT.
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New AI Buff Condition", menuName = "FishMMO/Character/NPC/AI/Conditions/Buff Condition")]
	public class AIBuffCondition : AIAbilityCondition
	{
		/// <summary>
		/// Which character to check for the buff/debuff.
		/// </summary>
		[Tooltip("Which character to check for the buff/debuff.")]
		public ConditionSubject Subject = ConditionSubject.Target;

		/// <summary>
		/// The template ID of the buff or debuff to look for.
		/// </summary>
		[Tooltip("Template ID of the buff or debuff to look for.")]
		public int BuffTemplateID;

		/// <summary>
		/// If true, the condition passes when the buff IS present.
		/// If false, the condition passes when the buff is NOT present.
		/// </summary>
		[Tooltip("True = buff must be present. False = buff must be absent.")]
		public bool RequirePresent = true;

		/// <summary>
		/// Evaluates whether the subject has (or lacks) the specified buff.
		/// If the subject has no <see cref="IBuffController"/>, it is treated as having no buffs.
		/// </summary>
		public override bool Evaluate(AIController controller, ICharacter self, ICharacter target)
		{
			ICharacter subject = Subject == ConditionSubject.Self ? self : target;
			if (subject == null)
				return false;

			if (!subject.TryGet(out IBuffController buffController))
				return !RequirePresent; // No buff controller → treat as "no buffs"

			bool hasBuff = buffController.Buffs.ContainsKey(BuffTemplateID);
			return RequirePresent ? hasBuff : !hasBuff;
		}
	}
}
