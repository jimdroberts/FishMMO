using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Proofs that an ability's role is readable from the ECA actions a designer attached to it,
	/// which is what lets archetypes stop naming abilities by template ID.
	/// </summary>
	/// <remarks>
	/// The interesting cases are the inferred ones. Whether an <see cref="ApplyHealAction"/> means
	/// "this heals" is not in doubt; whether a buff that trades armour for movement speed counts as
	/// help or harm is, and it is the answer an NPC acts on when deciding who to point a spell at.
	/// </remarks>
	[TestFixture]
	public class AIAbilityClassifierTests
	{
		/// <summary>Everything created during a test, destroyed afterwards.</summary>
		private readonly List<Object> created = new List<Object>();

		/// <summary>
		/// Drops every throwaway asset and clears the classifier's cache so one test's templates
		/// cannot answer another test's question.
		/// </summary>
		[TearDown]
		public void TearDown()
		{
			for (int i = 0; i < created.Count; ++i)
			{
				if (created[i] != null)
				{
					Object.DestroyImmediate(created[i]);
				}
			}
			created.Clear();
			AIAbilityClassifier.ClearCache();
		}

		/// <summary>
		/// Creates a ScriptableObject that will be cleaned up after the test.
		/// </summary>
		/// <typeparam name="T">The type to create.</typeparam>
		/// <returns>The new instance.</returns>
		private T Create<T>() where T : ScriptableObject
		{
			T instance = ScriptableObject.CreateInstance<T>();
			created.Add(instance);
			return instance;
		}

		/// <summary>
		/// Builds an ability template whose on-hit event runs the given actions.
		/// </summary>
		/// <param name="actions">The actions to attach.</param>
		/// <returns>The template.</returns>
		private AbilityTemplate TemplateWithHitActions(params BaseAction[] actions)
		{
			AbilityOnHitEvent hitEvent = Create<AbilityOnHitEvent>();
			hitEvent.OnConditionsMetActions = new List<BaseAction>(actions);

			AbilityTemplate template = Create<AbilityTemplate>();
			template.OnHitEvents = new List<AbilityOnHitEvent> { hitEvent };
			return template;
		}

		/// <summary>
		/// Builds a buff attribute modifier.
		/// </summary>
		/// <param name="value">The signed modifier value.</param>
		/// <returns>The modifier.</returns>
		private static BuffAttributeTemplate Modifier(int value)
		{
			return new BuffAttributeTemplate { Value = value };
		}

		// --- Direct actions ------------------------------------------------------------------

		/// <summary>
		/// An ability that deals damage reads as offensive.
		/// </summary>
		[Test]
		public void DamageAction_ClassifiesAsOffensiveDamage()
		{
			AbilityTemplate template = TemplateWithHitActions(new ApplyDamageAction());

			AIAbilityIntent intent = AIAbilityClassifier.Classify(template);

			Assert.IsTrue(intent.HasAny(AIAbilityIntent.Damage), "Expected Damage.");
			Assert.IsTrue(intent.IsOffensive(), "Damage must be offensive.");
			Assert.IsFalse(intent.IsSupportive(), "Damage must not be supportive.");
		}

		/// <summary>
		/// An ability that heals reads as supportive — the case the healer archetype used to need a
		/// hand-maintained ID list to answer.
		/// </summary>
		[Test]
		public void HealAction_ClassifiesAsSupportiveHeal()
		{
			AbilityTemplate template = TemplateWithHitActions(new ApplyHealAction());

			AIAbilityIntent intent = AIAbilityClassifier.Classify(template);

			Assert.IsTrue(intent.HasAny(AIAbilityIntent.Heal), "Expected Heal.");
			Assert.IsTrue(intent.IsSupportive(), "Heal must be supportive.");
			Assert.IsFalse(intent.IsOffensive(), "Heal must not be offensive.");
		}

		/// <summary>
		/// A taunt reads as threat manipulation, which is what the defender archetype leads with.
		/// </summary>
		[Test]
		public void TauntAction_ClassifiesAsThreat()
		{
			AbilityTemplate template = TemplateWithHitActions(new ApplyTauntAction());

			AIAbilityIntent intent = AIAbilityClassifier.Classify(template);

			Assert.IsTrue(intent.HasAny(AIAbilityIntent.Threat), "Expected Threat.");
			Assert.IsTrue(intent.IsOffensive(), "A taunt is aimed at an enemy.");
		}

		/// <summary>
		/// Interrupting a cast is control, not damage.
		/// </summary>
		[Test]
		public void InterruptAction_ClassifiesAsControl()
		{
			AbilityTemplate template = TemplateWithHitActions(new InterruptAction());

			AIAbilityIntent intent = AIAbilityClassifier.Classify(template);

			Assert.IsTrue(intent.HasAny(AIAbilityIntent.Control), "Expected Control.");
			Assert.IsFalse(intent.HasAny(AIAbilityIntent.Damage), "Interrupt deals no damage.");
		}

		/// <summary>
		/// An ability with several actions carries every intent they imply, rather than being
		/// forced into one category.
		/// </summary>
		[Test]
		public void CompoundAbility_CarriesEveryIntentItsActionsImply()
		{
			AbilityTemplate template = TemplateWithHitActions(
				new ApplyDamageAction(),
				new InterruptAction());

			AIAbilityIntent intent = AIAbilityClassifier.Classify(template);

			Assert.IsTrue(intent.HasAny(AIAbilityIntent.Damage), "Expected Damage.");
			Assert.IsTrue(intent.HasAny(AIAbilityIntent.Control), "Expected Control.");
		}

		/// <summary>
		/// The not-met branch of a trigger counts too: an NPC that only read the met branch would
		/// misjudge half of any ability with a fallback effect.
		/// </summary>
		[Test]
		public void ConditionsNotMetBranch_IsClassifiedToo()
		{
			AbilityOnHitEvent hitEvent = Create<AbilityOnHitEvent>();
			hitEvent.OnConditionsMetActions = new List<BaseAction>();
			hitEvent.OnConditionsNotMetActions = new List<BaseAction> { new ApplyDamageAction() };

			AbilityTemplate template = Create<AbilityTemplate>();
			template.OnHitEvents = new List<AbilityOnHitEvent> { hitEvent };

			Assert.IsTrue(AIAbilityClassifier.Classify(template).HasAny(AIAbilityIntent.Damage),
				"Actions on the conditions-not-met branch must be classified.");
		}

		// --- Buff direction ------------------------------------------------------------------

		/// <summary>
		/// A buff that raises attributes is help.
		/// </summary>
		[Test]
		public void PositiveAttributeBuff_ClassifiesAsBuff()
		{
			AttributeBuffTemplate buff = Create<AttributeBuffTemplate>();
			buff.BonusAttributes = new List<BuffAttributeTemplate> { Modifier(50) };

			AbilityTemplate template = TemplateWithHitActions(new ApplyBuffAction { BuffTemplate = buff });

			AIAbilityIntent intent = AIAbilityClassifier.Classify(template);

			Assert.IsTrue(intent.HasAny(AIAbilityIntent.Buff), "Expected Buff.");
			Assert.IsFalse(intent.HasAny(AIAbilityIntent.Debuff), "A positive buff is not a debuff.");
		}

		/// <summary>
		/// A buff that lowers attributes is harm, and belongs in the attack rotation rather than
		/// being cast on a friend.
		/// </summary>
		[Test]
		public void NegativeAttributeBuff_ClassifiesAsDebuff()
		{
			AttributeBuffTemplate buff = Create<AttributeBuffTemplate>();
			buff.BonusAttributes = new List<BuffAttributeTemplate> { Modifier(-30) };

			AbilityTemplate template = TemplateWithHitActions(new ApplyBuffAction { BuffTemplate = buff });

			AIAbilityIntent intent = AIAbilityClassifier.Classify(template);

			Assert.IsTrue(intent.HasAny(AIAbilityIntent.Debuff), "Expected Debuff.");
			Assert.IsTrue(intent.IsOffensive(), "A debuff is aimed at an enemy.");
		}

		/// <summary>
		/// A mixed buff is judged on the sum, not on the presence of a single negative entry —
		/// otherwise a plate-armour buff with a small speed penalty would be read as a curse.
		/// </summary>
		[Test]
		public void MixedAttributeBuff_IsJudgedOnTheNetChange()
		{
			AttributeBuffTemplate buff = Create<AttributeBuffTemplate>();
			buff.BonusAttributes = new List<BuffAttributeTemplate> { Modifier(100), Modifier(-5) };

			AbilityTemplate template = TemplateWithHitActions(new ApplyBuffAction { BuffTemplate = buff });

			Assert.IsTrue(AIAbilityClassifier.Classify(template).HasAny(AIAbilityIntent.Buff),
				"A net-positive buff is a buff despite a small penalty.");
		}

		/// <summary>
		/// A stun is crowd control, not a debuff, so a control-favouring personality can reach for
		/// it specifically.
		/// </summary>
		[Test]
		public void IncapacitatingStateBuff_ClassifiesAsControl()
		{
			StateBuffTemplate buff = Create<StateBuffTemplate>();
			buff.Flag = CharacterFlags.IsStunned;

			AbilityTemplate template = TemplateWithHitActions(new ApplyBuffAction { BuffTemplate = buff });

			Assert.IsTrue(AIAbilityClassifier.Classify(template).HasAny(AIAbilityIntent.Control),
				"A stun must classify as Control.");
		}

		/// <summary>
		/// A damage-over-time resource tick is damage, and must not be mistaken for a buff simply
		/// because it arrives as one.
		/// </summary>
		[Test]
		public void NegativeResourceTick_ClassifiesAsDamage()
		{
			ResourceTickBuffTemplate buff = Create<ResourceTickBuffTemplate>();
			buff.TickAttributes = new List<BuffAttributeTemplate> { Modifier(-10) };

			AbilityTemplate template = TemplateWithHitActions(new ApplyBuffAction { BuffTemplate = buff });

			AIAbilityIntent intent = AIAbilityClassifier.Classify(template);

			Assert.IsTrue(intent.HasAny(AIAbilityIntent.Damage), "A DoT deals damage.");
			Assert.IsFalse(intent.IsSupportive(), "A DoT is not supportive.");
		}

		/// <summary>
		/// A heal-over-time resource tick is healing, so a healer will use it on a wounded ally.
		/// </summary>
		[Test]
		public void PositiveResourceTick_ClassifiesAsHeal()
		{
			ResourceTickBuffTemplate buff = Create<ResourceTickBuffTemplate>();
			buff.TickAttributes = new List<BuffAttributeTemplate> { Modifier(12) };

			AbilityTemplate template = TemplateWithHitActions(new ApplyBuffAction { BuffTemplate = buff });

			Assert.IsTrue(AIAbilityClassifier.Classify(template).HasAny(AIAbilityIntent.Heal),
				"A HoT heals.");
		}

		/// <summary>
		/// A buff whose periodic effect is authored as ECA actions is read from those actions, not
		/// guessed at. This is the path a designer takes for anything more interesting than a flat
		/// number per tick.
		/// </summary>
		[Test]
		public void EcaAuthoredDamageOverTime_ClassifiesAsDamage()
		{
			BuffTickEvent tick = Create<BuffTickEvent>();
			tick.OnConditionsMetActions = new List<BaseAction> { new ApplyDamageAction() };

			ResourceTickBuffTemplate buff = Create<ResourceTickBuffTemplate>();
			buff.OnTickEvents = new List<BuffTickEvent> { tick };

			AbilityTemplate template = TemplateWithHitActions(new ApplyBuffAction { BuffTemplate = buff });

			AIAbilityIntent intent = AIAbilityClassifier.Classify(template);

			Assert.IsTrue(intent.HasAny(AIAbilityIntent.Damage), "An ApplyDamageAction on a buff tick is damage.");
			Assert.IsFalse(intent.IsSupportive(), "A DoT is not supportive.");
		}

		/// <summary>
		/// The same, in the other direction: a heal-over-time authored through ECA is supportive,
		/// so a healer will use it on a wounded ally.
		/// </summary>
		[Test]
		public void EcaAuthoredHealOverTime_ClassifiesAsHeal()
		{
			BuffTickEvent tick = Create<BuffTickEvent>();
			tick.OnConditionsMetActions = new List<BaseAction> { new ApplyHealAction() };

			ResourceTickBuffTemplate buff = Create<ResourceTickBuffTemplate>();
			buff.OnTickEvents = new List<BuffTickEvent> { tick };

			AbilityTemplate template = TemplateWithHitActions(new ApplyBuffAction { BuffTemplate = buff });

			Assert.IsTrue(AIAbilityClassifier.Classify(template).HasAny(AIAbilityIntent.Heal),
				"An ApplyHealAction on a buff tick heals.");
		}

		// --- Dispel direction ----------------------------------------------------------------

		/// <summary>
		/// A cleanse points at a friend.
		/// </summary>
		[Test]
		public void DebuffStrippingDispel_IsSupportive()
		{
			AbilityTemplate template = TemplateWithHitActions(
				new ApplyDispelAction { IncludeDebuffs = true, IncludeBuffs = false });

			AIAbilityIntent intent = AIAbilityClassifier.Classify(template);

			Assert.IsTrue(intent.HasAny(AIAbilityIntent.Dispel), "Expected Dispel.");
			Assert.IsTrue(intent.IsSupportive(), "Stripping debuffs helps the target.");
			Assert.IsFalse(intent.IsOffensive(), "A cleanse is not an attack.");
		}

		/// <summary>
		/// A purge points at an enemy. The flag alone cannot say which a dispel is, so the
		/// classifier resolves it from the action's own configuration.
		/// </summary>
		[Test]
		public void BuffStrippingDispel_IsOffensive()
		{
			AbilityTemplate template = TemplateWithHitActions(
				new ApplyDispelAction { IncludeDebuffs = false, IncludeBuffs = true });

			AIAbilityIntent intent = AIAbilityClassifier.Classify(template);

			Assert.IsTrue(intent.HasAny(AIAbilityIntent.Dispel), "Expected Dispel.");
			Assert.IsTrue(intent.IsOffensive(), "Stripping buffs hurts the target.");
		}

		// --- Fallbacks -----------------------------------------------------------------------

		/// <summary>
		/// An ability with no ECA actions classifies as nothing, which the attack rotation treats
		/// as usable. Content authored before classification existed must keep working.
		/// </summary>
		[Test]
		public void AbilityWithNoActions_ClassifiesAsNone()
		{
			AbilityTemplate template = Create<AbilityTemplate>();

			Assert.AreEqual(AIAbilityIntent.None, AIAbilityClassifier.Classify(template));
		}

		/// <summary>
		/// A null template is not an error; NPCs hold ability slots that may be empty.
		/// </summary>
		[Test]
		public void NullTemplate_ClassifiesAsNone()
		{
			Assert.AreEqual(AIAbilityIntent.None, AIAbilityClassifier.Classify((AbilityTemplate)null));
		}

		/// <summary>
		/// The manual override wins outright, which is the designer's remedy when the buff-sign
		/// heuristic reads an effect backwards.
		/// </summary>
		[Test]
		public void IntentOverride_ReplacesTheDerivedIntent()
		{
			AbilityTemplate template = TemplateWithHitActions(new ApplyHealAction());
			template.IntentOverride = AIAbilityIntent.Damage;

			AIAbilityIntent intent = AIAbilityClassifier.Classify(template);

			Assert.AreEqual(AIAbilityIntent.Damage, intent, "The override must replace, not augment.");
			Assert.IsFalse(intent.HasAny(AIAbilityIntent.Heal), "The derived Heal must be discarded.");
		}

		// --- What the attack rotation accepts ------------------------------------------------

		/// <summary>
		/// The point of the whole exercise: an NPC must not cast a heal at the thing it is fighting.
		/// </summary>
		[Test]
		public void PurelySupportiveAbility_IsRejectedByTheAttackRotation()
		{
			AbilityTemplate template = TemplateWithHitActions(new ApplyHealAction());
			template.AbilitySpawnTarget = AbilitySpawnTarget.Target;

			Assert.IsFalse(BaseAttackingState.IsEnemyAbility(new Ability(template)),
				"A heal aimed at another character must not enter the attack rotation.");
		}

		/// <summary>
		/// A self-cast shield stays in the rotation: the NPC aims it at itself, so using it
		/// mid-fight is right rather than absurd.
		/// </summary>
		[Test]
		public void SelfCastSupportAbility_StaysInTheAttackRotation()
		{
			AttributeBuffTemplate buff = Create<AttributeBuffTemplate>();
			buff.BonusAttributes = new List<BuffAttributeTemplate> { Modifier(75) };

			AbilityTemplate template = TemplateWithHitActions(new ApplyBuffAction { BuffTemplate = buff });
			template.AbilitySpawnTarget = AbilitySpawnTarget.Self;

			Assert.IsTrue(BaseAttackingState.IsEnemyAbility(new Ability(template)),
				"A self-buff is usable during a fight.");
		}

		/// <summary>
		/// An ability that both damages and heals stays in the rotation — the damage is the point.
		/// </summary>
		[Test]
		public void DrainAbility_StaysInTheAttackRotation()
		{
			AbilityTemplate template = TemplateWithHitActions(
				new ApplyDamageAction(),
				new ApplyHealAction());
			template.AbilitySpawnTarget = AbilitySpawnTarget.Target;

			Assert.IsTrue(BaseAttackingState.IsEnemyAbility(new Ability(template)),
				"A drain is an attack that happens to heal.");
		}

		/// <summary>
		/// An unclassifiable ability is allowed through rather than silently disarming the NPC
		/// that knows it.
		/// </summary>
		[Test]
		public void UnclassifiableAbility_StaysInTheAttackRotation()
		{
			AbilityTemplate template = Create<AbilityTemplate>();

			Assert.IsTrue(BaseAttackingState.IsEnemyAbility(new Ability(template)),
				"An ability with no recognised intent must remain usable.");
		}

		// --- Personality weighting -----------------------------------------------------------

		/// <summary>
		/// A personality biased toward control reaches for a stun over a nuke without either
		/// ability being named on the archetype.
		/// </summary>
		[Test]
		public void ControlPersonality_WeighsControlAbilitiesHigher()
		{
			AICombatPersonality personality = Create<AICombatPersonality>();
			personality.ControlWeight = 3.0f;
			personality.DamageWeight = 1.0f;

			StateBuffTemplate stun = Create<StateBuffTemplate>();
			stun.Flag = CharacterFlags.IsStunned;

			Ability control = new Ability(TemplateWithHitActions(new ApplyBuffAction { BuffTemplate = stun }));
			Ability nuke = new Ability(TemplateWithHitActions(new ApplyDamageAction()));

			Assert.Greater(personality.GetIntentWeight(control), personality.GetIntentWeight(nuke),
				"A control-focused personality must prefer its control abilities.");
		}

		/// <summary>
		/// An ability carrying several intents takes the strongest weight, not the product, so a
		/// merely-compound ability cannot out-score a specialised one on flag count alone.
		/// </summary>
		[Test]
		public void CompoundAbility_TakesTheStrongestIntentWeightNotTheProduct()
		{
			AICombatPersonality personality = Create<AICombatPersonality>();
			personality.DamageWeight = 2.0f;
			personality.ControlWeight = 2.0f;

			Ability compound = new Ability(TemplateWithHitActions(
				new ApplyDamageAction(),
				new InterruptAction()));

			Assert.AreEqual(2.0f, personality.GetIntentWeight(compound), 0.001f,
				"Intent weights must not compound.");
		}

		/// <summary>
		/// An ability with no recognised intent is left unbiased rather than zeroed, which would
		/// remove it from selection entirely.
		/// </summary>
		[Test]
		public void UnclassifiableAbility_IsWeightedNeutrally()
		{
			AICombatPersonality personality = Create<AICombatPersonality>();

			Ability plain = new Ability(Create<AbilityTemplate>());

			Assert.AreEqual(1.0f, personality.GetIntentWeight(plain), 0.001f,
				"An unclassifiable ability must be neither favoured nor suppressed.");
		}
	}
}
