using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Combat style archetype that broadly describes how the NPC approaches combat.
	/// Designers pick a style per personality asset; the <see cref="AICombatPersonality"/>
	/// weights then fine-tune the behaviour.
	/// </summary>
	public enum NPCCombatStyle
	{
		/// <summary>Default balanced approach — no strong bias.</summary>
		Balanced = 0,
		/// <summary>Prefers closing to melee range and high-pressure attacks.</summary>
		Aggressive,
		/// <summary>Prefers keeping distance, kiting, and control abilities.</summary>
		Defensive,
		/// <summary>Avoids risk, retreats earlier, favours safe abilities.</summary>
		Cautious,
		/// <summary>All-out damage, ignores self-preservation. Never retreats.</summary>
		Berserker,
	}

	/// <summary>
	/// Ability category inferred at runtime from an ability's template data.
	/// Used by <see cref="AICombatPersonality"/> to apply personality weights.
	/// </summary>
	public enum AbilityCategory
	{
		/// <summary>Unable to classify.</summary>
		Unknown = 0,
		/// <summary>Short-range, physical-oriented ability (PointBlank, low range).</summary>
		Melee,
		/// <summary>Long-range projectile or targeted ability.</summary>
		Ranged,
		/// <summary>Area-of-effect ability (HitCount &gt; 1 or ground-targeted).</summary>
		AOE,
		/// <summary>Self-targeted buff, shield, or utility ability.</summary>
		Support,
	}

	/// <summary>
	/// Data-driven combat personality asset that makes two NPCs with the same ability set
	/// behave differently in combat. Assign to <see cref="AIController.Personality"/>.
	/// <para>
	/// The personality provides per-<see cref="AbilityCategory"/> score multipliers that
	/// <see cref="AIController.PickBestAbility"/> applies on top of the default scoring.
	/// Two warriors sharing the same ability list but with different personalities will
	/// favour different abilities and positioning.
	/// </para>
	/// <para>
	/// <b>Range thresholds</b> control how abilities are classified. Abilities whose
	/// <see cref="Ability.Range"/> falls below <see cref="MeleeRangeThreshold"/> are
	/// considered melee; those above are ranged. Abilities with
	/// <see cref="AbilityTemplate.HitCount"/> greater than 1 or grounded spawn targets
	/// are classified as AOE. Self-targeted abilities are classified as Support.
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New Combat Personality", menuName = "FishMMO/Character/NPC/AI/Combat Personality", order = 10)]
	public class AICombatPersonality : ScriptableObject
	{
		[Header("Style")]
		[Tooltip("The broad combat archetype. Affects retreat threshold behaviour.")]
		public NPCCombatStyle Style = NPCCombatStyle.Balanced;

		[Header("Ability Category Weights")]
		[Tooltip("Score multiplier for melee-classified abilities. >1 = prefer, <1 = avoid.")]
		[Range(0f, 5f)]
		public float MeleeWeight = 1.0f;

		[Tooltip("Score multiplier for ranged-classified abilities.")]
		[Range(0f, 5f)]
		public float RangedWeight = 1.0f;

		[Tooltip("Score multiplier for AOE-classified abilities.")]
		[Range(0f, 5f)]
		public float AOEWeight = 1.0f;

		[Tooltip("Score multiplier for support/self-buff abilities.")]
		[Range(0f, 5f)]
		public float SupportWeight = 1.0f;

		[Header("Classification Thresholds")]
		[Tooltip("Abilities with range <= this value are considered melee. Above this = ranged.")]
		public float MeleeRangeThreshold = 4f;

		[Tooltip("Abilities with HitCount > this value are considered AOE (multi-target).")]
		public int AOEHitCountThreshold = 1;

		[Header("Combat Behavior")]
		[Tooltip("Health percentage (0-1) at which this NPC considers retreating. " +
				 "Berserker style ignores this. 0 = never retreat from low health.")]
		[Range(0f, 1f)]
		public float RetreatHealthThreshold = 0f;

		[Tooltip("Bonus score added to abilities when health is above retreat threshold. " +
				 "Encourages aggressive play while healthy.")]
		public float HealthyAggressionBonus = 0f;

		[Tooltip("Bonus score added to support/self-buff abilities when health is below retreat threshold. " +
				 "Encourages defensive play when hurt.")]
		public float LowHealthSupportBonus = 100f;

		/// <summary>
		/// Classifies an ability into a <see cref="AbilityCategory"/> based on its template data.
		/// Uses range, spawn target, hit count, and ability type to infer the category.
		/// </summary>
		/// <param name="ability">The ability instance to classify.</param>
		/// <returns>The inferred category for personality scoring.</returns>
		public AbilityCategory ClassifyAbility(Ability ability)
		{
			if (ability == null || ability.Template == null)
				return AbilityCategory.Unknown;

			AbilityTemplate template = ability.Template;

			// Self-targeted abilities are support/utility.
			if (template.AbilitySpawnTarget == AbilitySpawnTarget.Self)
				return AbilityCategory.Support;

			// Ground-targeted or multi-hit abilities are AOE.
			if (template.HitCount > AOEHitCountThreshold)
				return AbilityCategory.AOE;

			// Grounded variants (GroundedPhysical, GroundedMagic) are typically AOE ground effects.
			if (template.Type == AbilityType.GroundedPhysical ||
				template.Type == AbilityType.GroundedMagic)
				return AbilityCategory.AOE;

			// PointBlank spawn = melee regardless of range.
			if (template.AbilitySpawnTarget == AbilitySpawnTarget.PointBlank)
				return AbilityCategory.Melee;

			// Use range to distinguish melee vs ranged.
			float range = ability.Range;
			if (range <= MeleeRangeThreshold)
				return AbilityCategory.Melee;

			return AbilityCategory.Ranged;
		}

		/// <summary>
		/// Returns a score multiplier for the given ability based on this personality's weights.
		/// Called by <see cref="AIController.PickBestAbility"/> to bias ability selection.
		/// </summary>
		/// <param name="ability">The ability being scored.</param>
		/// <returns>A multiplier (typically 0.1–5.0) to apply to the ability's base score.</returns>
		public float GetWeight(Ability ability)
		{
			AbilityCategory category = ClassifyAbility(ability);

			switch (category)
			{
				case AbilityCategory.Melee: return MeleeWeight;
				case AbilityCategory.Ranged: return RangedWeight;
				case AbilityCategory.AOE: return AOEWeight;
				case AbilityCategory.Support: return SupportWeight;
				default: return 1.0f;
			}
		}

		/// <summary>
		/// Computes a personality-aware bonus score for the given ability, factoring in
		/// the NPC's current health and combat style.
		/// </summary>
		/// <param name="ability">The ability being scored.</param>
		/// <param name="healthPercent">Current health as a fraction (0-1). Pass 1 if unknown.</param>
		/// <returns>An additive bonus to the ability's base score.</returns>
		public float GetBonusScore(Ability ability, float healthPercent)
		{
			float bonus = 0f;

			// When healthy, aggressive personalities get a bonus on offensive abilities.
			if (healthPercent > RetreatHealthThreshold && HealthyAggressionBonus > 0f)
			{
				AbilityCategory cat = ClassifyAbility(ability);
				if (cat == AbilityCategory.Melee || cat == AbilityCategory.Ranged || cat == AbilityCategory.AOE)
				{
					bonus += HealthyAggressionBonus;
				}
			}

			// When hurt, encourage support/self-buff usage.
			if (RetreatHealthThreshold > 0f && healthPercent <= RetreatHealthThreshold)
			{
				AbilityCategory cat = ClassifyAbility(ability);
				if (cat == AbilityCategory.Support)
				{
					bonus += LowHealthSupportBonus;
				}
			}

			return bonus;
		}

		/// <summary>
		/// Returns true if this personality's style allows retreating, and the NPC's health
		/// has dropped below <see cref="RetreatHealthThreshold"/>.
		/// Berserker style never retreats from low health.
		/// </summary>
		/// <param name="healthPercent">Current health as a fraction (0-1).</param>
		/// <returns>True if the NPC should consider retreating.</returns>
		public bool ShouldRetreat(float healthPercent)
		{
			if (Style == NPCCombatStyle.Berserker)
				return false;

			if (RetreatHealthThreshold <= 0f)
				return false;

			return healthPercent <= RetreatHealthThreshold;
		}
	}
}