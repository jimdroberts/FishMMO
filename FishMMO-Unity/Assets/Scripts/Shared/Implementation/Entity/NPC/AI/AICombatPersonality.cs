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
		/// <summary>
		/// Cowardly. Breaks and runs as soon as it is meaningfully hurt, and keeps running.
		/// A Pathetic personality is guaranteed to have a retreat threshold even if the designer
		/// left the field at zero — see <see cref="EffectiveRetreatHealthThreshold"/>.
		/// </summary>
		Pathetic,
		/// <summary>
		/// Never breaks. Fights to the last point of health and holds its threat target.
		/// Functionally "Berserker without the target chaos".
		/// </summary>
		Determined,
		/// <summary>
		/// Berserk and unfocused: never retreats, ignores the threat table, and re-picks a random
		/// living enemy at <see cref="RampageRetargetChance"/> on every re-evaluation.
		/// </summary>
		Rampaging,
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

		[Header("Ability Intent Weights")]
		/* Category weights above describe *delivery* — how an ability reaches its target. These
		 * describe *purpose*, read straight out of the ability's ECA action graph by
		 * AIAbilityClassifier. The two are orthogonal and both apply: a crowd controller wants
		 * ranged delivery and controlling purpose, and neither weight alone says that.
		 *
		 * This is what replaced naming abilities by template ID on the archetype. A personality
		 * that raises ControlWeight prefers whatever in its spellbook actually roots and stuns,
		 * including the ability a designer adds tomorrow, with nothing to keep in step by hand. */

		[Tooltip("Score multiplier for abilities that deal damage.")]
		[Range(0f, 5f)]
		public float DamageWeight = 1.0f;

		[Tooltip("Score multiplier for abilities that restore health.")]
		[Range(0f, 5f)]
		public float HealWeight = 1.0f;

		[Tooltip("Score multiplier for abilities that stun, root, freeze, interrupt or knock back.")]
		[Range(0f, 5f)]
		public float ControlWeight = 1.0f;

		[Tooltip("Score multiplier for abilities that weaken a target.")]
		[Range(0f, 5f)]
		public float DebuffWeight = 1.0f;

		[Tooltip("Score multiplier for abilities that strengthen a friend.")]
		[Range(0f, 5f)]
		public float BuffWeight = 1.0f;

		[Tooltip("Score multiplier for taunts and other threat manipulation.")]
		[Range(0f, 5f)]
		public float ThreatWeight = 1.0f;

		[Header("Classification Thresholds")]
		[Tooltip("Abilities with range <= this value are considered melee. Above this = ranged.")]
		public float MeleeRangeThreshold = 4f;

		[Tooltip("Abilities with HitCount > this value are considered AOE (multi-target).")]
		public int AOEHitCountThreshold = 1;

		[Header("Targeting")]
		[Tooltip("How this NPC chooses between available enemies. Rampaging style forces Random.")]
		public AITargetingMode Targeting = AITargetingMode.Threat;

		[Tooltip("Chance (0-1) a Rampaging NPC switches to a new random enemy on each re-evaluation.")]
		[Range(0f, 1f)]
		public float RampageRetargetChance = 0.5f;

		[Header("Combat Behavior")]
		[Tooltip("Health percentage (0-1) at which this NPC considers retreating. " +
				 "Berserker, Determined and Rampaging styles ignore this. 0 = never retreat.")]
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
		/// Returns a score multiplier for the given ability, combining how it is delivered with
		/// what it does.
		/// </summary>
		/// <remarks>
		/// Called by <see cref="AIController.PickScoredAbility"/> to bias ability selection. The
		/// delivery weight and the intent weight multiply: an archer personality that favours
		/// ranged delivery and a caster personality that favours control can share an ability list
		/// and still reach for different things in it.
		/// </remarks>
		/// <param name="ability">The ability being scored.</param>
		/// <returns>A multiplier (typically 0.1–5.0) to apply to the ability's base score.</returns>
		public float GetWeight(Ability ability)
		{
			return GetCategoryWeight(ability) * GetIntentWeight(ability);
		}

		/// <summary>
		/// Returns the multiplier for how an ability is delivered — melee, ranged, area, or self.
		/// </summary>
		/// <param name="ability">The ability being scored.</param>
		/// <returns>The category multiplier.</returns>
		public float GetCategoryWeight(Ability ability)
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
		/// Returns the multiplier for what an ability does, derived from its ECA actions.
		/// </summary>
		/// <remarks>
		/// The strongest matching weight, not the product of all of them. Abilities routinely carry
		/// several intents — a nuke that also applies a burn is Damage and Debuff — and multiplying
		/// would let a merely-compound ability out-score a genuinely specialised one purely for
		/// having more flags set.
		/// </remarks>
		/// <param name="ability">The ability being scored.</param>
		/// <returns>The intent multiplier, or 1 for an ability with no recognised intent.</returns>
		public float GetIntentWeight(Ability ability)
		{
			AIAbilityIntent intent = AIAbilityClassifier.Classify(ability);
			if (intent == AIAbilityIntent.None)
			{
				return 1.0f;
			}

			float weight = 0f;
			bool matched = false;

			if (intent.HasAny(AIAbilityIntent.Damage)) { weight = Mathf.Max(weight, DamageWeight); matched = true; }
			if (intent.HasAny(AIAbilityIntent.Heal)) { weight = Mathf.Max(weight, HealWeight); matched = true; }
			if (intent.HasAny(AIAbilityIntent.Control)) { weight = Mathf.Max(weight, ControlWeight); matched = true; }
			if (intent.HasAny(AIAbilityIntent.Debuff)) { weight = Mathf.Max(weight, DebuffWeight); matched = true; }
			if (intent.HasAny(AIAbilityIntent.Buff)) { weight = Mathf.Max(weight, BuffWeight); matched = true; }
			if (intent.HasAny(AIAbilityIntent.Threat)) { weight = Mathf.Max(weight, ThreatWeight); matched = true; }

			// Revive, Summon and Utility carry no weight of their own; leave them unbiased rather
			// than letting an unweighted intent silently zero the score.
			return matched ? weight : 1.0f;
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
			float retreatThreshold = EffectiveRetreatHealthThreshold;

			AIAbilityIntent intent = AIAbilityClassifier.Classify(ability);
			AbilityCategory cat = ClassifyAbility(ability);

			if (healthPercent > retreatThreshold && HealthyAggressionBonus > 0f)
			{
				/* Intent first, delivery as the fallback. An ability whose ECA graph says it hurts
				 * people is offensive whatever shape it is delivered in, and an ability with no
				 * classifiable actions still gets the old range-based answer rather than nothing. */
				bool offensive = intent.IsOffensive() ||
								 (intent == AIAbilityIntent.None &&
								  (cat == AbilityCategory.Melee || cat == AbilityCategory.Ranged || cat == AbilityCategory.AOE));

				if (offensive)
				{
					bonus += HealthyAggressionBonus;
				}
			}

			// When hurt, encourage healing and self-protection over pressing the attack.
			if (retreatThreshold > 0f && healthPercent <= retreatThreshold)
			{
				if (intent.HasAny(AIAbilityIntent.Heal | AIAbilityIntent.Buff) || cat == AbilityCategory.Support)
				{
					bonus += LowHealthSupportBonus;
				}
			}

			return bonus;
		}

		/// <summary>
		/// Fallback retreat threshold applied to a <see cref="NPCCombatStyle.Pathetic"/>
		/// personality whose <see cref="RetreatHealthThreshold"/> was left at zero.
		/// </summary>
		/// <remarks>
		/// A "pathetic" archetype that silently never flees because a serialized field defaulted
		/// to 0 is the exact failure this guards against: the style is the designer's stated
		/// intent, so it wins over an unset number rather than being quietly ignored.
		/// </remarks>
		public const float PATHETIC_DEFAULT_RETREAT_THRESHOLD = 0.5f;

		/// <summary>
		/// True when this style is constitutionally incapable of fleeing, whatever
		/// <see cref="RetreatHealthThreshold"/> says.
		/// </summary>
		public bool IsFearless => Style == NPCCombatStyle.Berserker ||
								  Style == NPCCombatStyle.Determined ||
								  Style == NPCCombatStyle.Rampaging;

		/// <summary>
		/// The retreat threshold actually used at runtime, after style overrides.
		/// Returns 0 for fearless styles, and the Pathetic fallback for a Pathetic personality
		/// left unconfigured.
		/// </summary>
		public float EffectiveRetreatHealthThreshold
		{
			get
			{
				if (IsFearless)
					return 0f;

				if (Style == NPCCombatStyle.Pathetic && RetreatHealthThreshold <= 0f)
					return PATHETIC_DEFAULT_RETREAT_THRESHOLD;

				return RetreatHealthThreshold;
			}
		}

		/// <summary>
		/// The targeting mode actually used at runtime, after style overrides.
		/// <see cref="NPCCombatStyle.Rampaging"/> always targets randomly — that unfocused
		/// re-targeting is what the style means.
		/// </summary>
		public AITargetingMode TargetingMode =>
			Style == NPCCombatStyle.Rampaging ? AITargetingMode.Random : Targeting;

		/// <summary>
		/// Chance this NPC abandons its current target for a fresh random one on each mid-combat
		/// re-evaluation. Non-zero only for <see cref="NPCCombatStyle.Rampaging"/>.
		/// </summary>
		public float EffectiveRetargetChance =>
			Style == NPCCombatStyle.Rampaging ? RampageRetargetChance : 0f;

		/// <summary>
		/// Returns true if this personality's style allows retreating, and the NPC's health
		/// has dropped to or below its <see cref="EffectiveRetreatHealthThreshold"/>.
		/// </summary>
		/// <param name="healthPercent">Current health as a fraction (0-1).</param>
		/// <returns>True if the NPC should break off and flee.</returns>
		public bool ShouldRetreat(float healthPercent)
		{
			float threshold = EffectiveRetreatHealthThreshold;
			if (threshold <= 0f)
				return false;

			return healthPercent <= threshold;
		}
	}
}