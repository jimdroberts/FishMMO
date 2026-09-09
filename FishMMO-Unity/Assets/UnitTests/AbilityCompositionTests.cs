using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// One composition, used by the crafting preview, the learned ability, and the base ability's
	/// own description.
	/// </summary>
	/// <remarks>
	/// <para>
	/// An ability's numbers used to be worked out in three places. <c>Ability</c> summed the
	/// template, the events baked into the template, and the crafted ones. The template's tooltip
	/// summed the template and whatever the crafting panel handed it — <b>ignoring the template's
	/// own events</b>. So a base ability whose bundled movement effect supplies its speed
	/// advertised a preview the crafted ability would never match, and the player only found out
	/// after paying.
	/// </para>
	/// <para>
	/// <see cref="AbilitySummary"/> is now the only rule. These tests pin that it agrees with
	/// <see cref="Ability"/>, that a base block excludes only the player's own choices, and that
	/// the deltas name the right direction.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AbilityCompositionTests
	{
		private readonly List<ScriptableObject> created = new List<ScriptableObject>();

		[TearDown]
		public void TearDown()
		{
			foreach (ScriptableObject so in created)
			{
				if (so != null)
				{
					Object.DestroyImmediate(so);
				}
			}
			created.Clear();
		}

		private AbilityTemplate NewTemplate(string name)
		{
			AbilityTemplate template = ScriptableObject.CreateInstance<AbilityTemplate>();
			template.name = name;
			created.Add(template);
			return template;
		}

		/// <summary>
		/// A tick event with a real ID.
		/// </summary>
		/// <remarks>
		/// <c>AddToCache</c> is what assigns one, and the shipped path is the addressables loader
		/// at boot — an instance created here would otherwise carry zero, which is the id that
		/// means "not registered" rather than a real identity.
		/// </remarks>
		private AbilityOnTickEvent NewTickEvent(string name)
		{
			AbilityOnTickEvent abilityEvent = ScriptableObject.CreateInstance<AbilityOnTickEvent>();
			abilityEvent.name = name;
			abilityEvent.AddToCache(name);
			created.Add(abilityEvent);
			return abilityEvent;
		}

		[Test]
		public void LearningRaisesTheKnowledgeEventOnceAndMarksTheCharacterDirty()
		{
			/* Two behaviours the scroll grant depends on. The event is what puts the entry in the
			 * abilities panel — learning used to change a HashSet and tell nobody — and the dirty
			 * mark is what gets the knowledge into the character save, which never covered it.
			 * Both must fire for something NEW only: a re-learn that raised the event would add a
			 * second row, and one that marked dirty would rewrite the table for nothing. */
			GameObject host = new GameObject("KnowledgeController");
			try
			{
				AbilityController controller = host.AddComponent<AbilityController>();

				/* OnAwake is what allocates the knowledge sets, and FishNet calls it on spawn —
				 * which nothing here does. Invoking it directly is the whole initialisation this
				 * test needs; it touches no networking. */
				controller.OnAwake();

				AbilityTemplate template = NewTemplate("Taught");
				int raised = 0;
				controller.OnAddKnownAbility += _ => ++raised;

				controller.LearnBaseAbility(template);
				LogAssert.AreEqual(1, raised, "learning something new announces it");
				LogAssert.IsTrue(controller.KnowledgeDirty, "and marks the character for the next save");

				controller.KnowledgeDirty = false;
				controller.LearnBaseAbility(template);
				LogAssert.AreEqual(1, raised, "learning it again announces nothing");
				LogAssert.IsFalse(controller.KnowledgeDirty, "and does not dirty the character");
			}
			finally
			{
				Object.DestroyImmediate(host);
			}
		}

		[Test]
		public void TheBaseBlockIncludesTheEventsTheTemplateShipsWith()
		{
			/* The defect this class exists to close. The bundled event's speed is part of the base
			 * ability the player is choosing, not part of what they added to it. */
			AbilityTemplate template = NewTemplate("Fireball");
			template.Speed = 0.0f;
			template.LifeTime = 2.0f;

			AbilityOnTickEvent movement = NewTickEvent("Projectile Move");
			movement.Speed = 12.0f;
			template.OnTickEvents.Add(movement);

			AbilitySummary summary = AbilitySummary.Compose(template);

			LogAssert.AreEqual(12.0f, summary.BaseStats.Speed,
				"a template's own events contribute to the base ability's numbers");
			LogAssert.AreEqual(24.0f, summary.BaseStats.Range,
				"and therefore to its range, which is speed times lifetime");
		}

		[Test]
		public void ComposedStatsMatchTheAbilityTheServerWouldBuild()
		{
			/* The preview and the thing it previews, compared directly. Two calculations that are
			 * meant to agree only stay in agreement if something checks. */
			AbilityTemplate template = NewTemplate("Fireball");
			template.ActivationTime = 0.8f;
			template.Cooldown = 2.5f;
			template.LifeTime = 2.0f;

			AbilityOnTickEvent movement = NewTickEvent("Projectile Move");
			movement.Speed = 12.0f;
			template.OnTickEvents.Add(movement);

			AbilityOnTickEvent heavy = NewTickEvent("Heavy");
			heavy.Cooldown = 1.5f;
			heavy.ActivationTime = 0.4f;

			AbilitySummary summary = AbilitySummary.Compose(template, new List<ITooltip> { heavy });
			Ability ability = new Ability(1L, template, new List<int> { heavy.ID });

			LogAssert.AreEqual(ability.ActivationTime, summary.Stats.ActivationTime, "cast time must agree");
			LogAssert.AreEqual(ability.Cooldown, summary.Stats.Cooldown, "cooldown must agree");
			LogAssert.AreEqual(ability.Speed, summary.Stats.Speed, "speed must agree");
			LogAssert.AreEqual(ability.LifeTime, summary.Stats.LifeTime, "lifetime must agree");
			LogAssert.AreEqual(ability.Range, summary.Stats.Range, "and so must the range they imply");
		}

		[Test]
		public void CraftedEffectsAreSeparatedFromBundledOnes()
		{
			AbilityTemplate template = NewTemplate("Fireball");
			AbilityOnTickEvent bundled = NewTickEvent("Projectile Move");
			template.OnTickEvents.Add(bundled);

			AbilityOnTickEvent chosen = NewTickEvent("Heavy");

			AbilitySummary summary = AbilitySummary.Compose(template, new List<ITooltip> { chosen });

			LogAssert.AreEqual(1, summary.TemplateEvents.Count, "the bundled effect is the template's");
			LogAssert.AreEqual(1, summary.CraftedEvents.Count, "the chosen effect is the player's");
			LogAssert.AreEqual("Heavy", summary.CraftedEvents[0].name, "and they are not confused with each other");
		}

		[Test]
		public void ADeltasDirectionDependsOnTheStatNotItsSign()
		{
			/* More range is good; more cooldown is not. Deciding this from the sign alone paints a
			 * penalty green, which is worse than not colouring it at all. */
			LogAssert.AreEqual(TooltipTone.Good, AbilitySummary.ToneForDelta(2.0f, moreIsBetter: true),
				"more of a good thing reads as an improvement");
			LogAssert.AreEqual(TooltipTone.Bad, AbilitySummary.ToneForDelta(2.0f, moreIsBetter: false),
				"more cooldown reads as a penalty");
			LogAssert.AreEqual(TooltipTone.Good, AbilitySummary.ToneForDelta(-2.0f, moreIsBetter: false),
				"and less of it reads as an improvement");
			LogAssert.AreEqual(TooltipTone.Neutral, AbilitySummary.ToneForDelta(0.0f, moreIsBetter: true),
				"an unchanged stat has no tone");
		}

		[Test]
		public void ThePreviewCarriesWhatEachEffectChanged()
		{
			AbilityTemplate template = NewTemplate("Fireball");
			template.Cooldown = 2.5f;

			AbilityOnTickEvent heavy = NewTickEvent("Heavy");
			heavy.Cooldown = 1.5f;

			AbilitySummary summary = AbilitySummary.Compose(template, new List<ITooltip> { heavy });
			TooltipContent content = new TooltipContent();
			summary.BuildTooltip(content, showDeltas: true);

			bool found = false;
			foreach (TooltipRow row in content.Rows)
			{
				if (row.Kind != TooltipRowKind.Stat || row.Label != "Cooldown")
				{
					continue;
				}
				found = true;
				LogAssert.AreEqual("4s", row.Value, "the composed cooldown is what the ability will have");
				LogAssert.AreEqual("+1.5s", row.Delta, "and the delta is what the chosen effect added");
				LogAssert.AreEqual(TooltipTone.Bad, row.DeltaTone, "which is a penalty, and reads as one");
			}
			LogAssert.IsTrue(found, "the preview must carry a cooldown row");
		}

		[Test]
		public void TheCraftPriceSumsTheBaseAndEveryChosenEffect()
		{
			AbilityTemplate template = NewTemplate("Fireball");
			template.Price = 100;

			AbilityOnTickEvent first = NewTickEvent("First");
			first.Price = 25;
			AbilityOnTickEvent second = NewTickEvent("Second");
			second.Price = 40;

			AbilitySummary summary = AbilitySummary.Compose(template, new List<ITooltip> { first, second });

			LogAssert.AreEqual(165, summary.CraftPrice, "the craft costs the base plus every effect");
		}

		[Test]
		public void ATypeOverrideReplacesTheTypeRatherThanAddingToIt()
		{
			AbilityTemplate template = NewTemplate("Fireball");
			template.Type = AbilityType.Magic;

			AbilityTypeOverrideEventType typeOverride = ScriptableObject.CreateInstance<AbilityTypeOverrideEventType>();
			typeOverride.name = "Aerial";
			typeOverride.OverrideAbilityType = AbilityType.AerialMagic;
			created.Add(typeOverride);

			AbilitySummary summary = AbilitySummary.Compose(template, new List<ITooltip> { typeOverride });

			LogAssert.AreEqual(AbilityType.AerialMagic, summary.Type, "the override wins outright");
			LogAssert.IsTrue(summary.HasCraftedEvents, "and counts as something the player chose");
		}

		[Test]
		public void AZeroStatIsOmittedUnlessCraftingChangedIt()
		{
			/* "Speed: 0m/s" on every melee strike is noise. A stat crafting drove TO zero is not:
			 * that is a change the player made and has to be able to see. */
			AbilityTemplate template = NewTemplate("Strike");
			template.Cooldown = 1.0f;

			TooltipContent plain = new TooltipContent();
			AbilitySummary.Compose(template).BuildTooltip(plain);
			LogAssert.IsFalse(HasStat(plain, "Speed"), "an ability with no speed does not advertise one");

			AbilityOnTickEvent push = NewTickEvent("Push");
			push.Speed = 6.0f;
			TooltipContent crafted = new TooltipContent();
			AbilitySummary.Compose(template, new List<ITooltip> { push }).BuildTooltip(crafted, showDeltas: true);
			LogAssert.IsTrue(HasStat(crafted, "Speed"), "an effect that grants speed makes it worth showing");
		}

		/// <summary>Whether the content carries a stat row with the given label.</summary>
		private static bool HasStat(TooltipContent content, string label)
		{
			foreach (TooltipRow row in content.Rows)
			{
				if (row.Kind == TooltipRowKind.Stat && row.Label == label)
				{
					return true;
				}
			}
			return false;
		}
	}
}
