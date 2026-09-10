using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Item attribute ranges are authored INCLUSIVE, and the starting kit still resolves.
	/// </summary>
	/// <remarks>
	/// Resistance is a 1:1 flat reduction by design, so a character's Armor is subtracted whole
	/// from every physical hit, with a floor of zero. Authored tiers are therefore tight ranges at
	/// the small end, and an exclusive upper bound collapsed every one of them to its minimum —
	/// that is what most of this fixture pins.
	/// <para>
	/// It used to also assert a ceiling: that the starting kit's Armor stayed below the weakest
	/// shipped melee ability, so the starting enemy could still land damage. That is NOT a rule.
	/// The starting kit is deliberately overpowered placeholder content, orcs are deliberately low
	/// tier, and later enemy types are meant to make the kit look weak. The ceiling is gone; what
	/// remains is that the kit resolves and that Armor is still the resistance melee uses.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ItemAttributeRollTests
	{
		private static int RollInclusive(DeterministicRNG rng, int min, int max)
		{
			MethodInfo method = typeof(ItemGenerator).GetMethod("RollInclusive",
				BindingFlags.NonPublic | BindingFlags.Static);
			Assert.That(method, Is.Not.Null, "ItemGenerator.RollInclusive must exist.");
			return (int)method.Invoke(null, new object[] { rng, min, max });
		}

		[Test]
		public void AuthoredMaximum_IsReachable()
		{
			// The whole point: the authored maximum must actually come up. Under the old exclusive
			// bound a 1..3 range produced only 1 and 2.
			HashSet<int> seen = new HashSet<int>();
			for (int seed = 0; seed < 400; ++seed)
			{
				seen.Add(RollInclusive(new DeterministicRNG(seed), 1, 3));
			}
			Assert.That(seen, Is.EquivalentTo(new[] { 1, 2, 3 }),
				"An authored 1..3 range must produce 1, 2 AND 3.");
		}

		[Test]
		public void TightTierRanges_DoNotCollapseToTheirMinimum()
		{
			// The ranges a 1:1 resistance scale is authored with: cloth 0..1, leather 1..2.
			foreach ((int min, int max) in new[] { (0, 1), (1, 2), (2, 3) })
			{
				HashSet<int> seen = new HashSet<int>();
				for (int seed = 0; seed < 400; ++seed)
				{
					seen.Add(RollInclusive(new DeterministicRNG(seed), min, max));
				}
				Assert.That(seen, Is.EquivalentTo(new[] { min, max }),
					$"A {min}..{max} tier must produce both ends, not just {min}.");
			}
		}

		[Test]
		public void SinglePointAndInvertedRanges_ReturnTheAuthoredValue()
		{
			Assert.That(RollInclusive(new DeterministicRNG(1), 4, 4), Is.EqualTo(4),
				"min == max is the authored value, not an error.");
			Assert.That(RollInclusive(new DeterministicRNG(1), 7, 3), Is.EqualTo(7),
				"An inverted range falls back to the minimum rather than throwing.");
			Assert.That(RollInclusive(new DeterministicRNG(1), 0, 0), Is.EqualTo(0),
				"A zero tier grants nothing.");
		}

		[Test]
		public void MaximumIntegerRange_DoesNotOverflowTheExclusiveBound()
		{
			int value = RollInclusive(new DeterministicRNG(3), int.MaxValue - 2, int.MaxValue);
			Assert.That(value, Is.GreaterThanOrEqualTo(int.MaxValue - 2),
				"int.MaxValue as the authored maximum must not wrap the bound negative.");
		}

		/// <summary>
		/// The configured starting equipment resolves, and Armor still resists Punch.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This asserted that the starting kit's Armor stayed BELOW Punch's physical damage, on the
		/// grounds that resistance is a 1:1 flat reduction with a floor of zero, so a kit at or above
		/// the damage makes a new character immune to it. The arithmetic is real — five pieces at 3
		/// Armor against 5 damage is zero through — but the conclusion was not: the starting kit is
		/// deliberately overpowered, orcs are deliberately low tier, and harder enemies later are meant
		/// to make this kit look weak. It is placeholder content that will not reach the main game.
		/// </para>
		/// <para>
		/// So the ceiling is gone and the two things that ARE invariants stay. The kit must resolve to
		/// real templates, which catches a rename or a re-hash breaking character creation silently.
		/// And Punch must still deal physical damage that Armor resists, which catches the damage type
		/// being unhooked from its resistance. Neither of those is a balance opinion.
		/// </para>
		/// <para>
		/// It never actually held the line it claimed to. Template IDs are assigned by
		/// <c>AddToCache</c>, which nothing calls in EditMode, so every asset compared as ID 0, the
		/// piece list came back empty, and the assertion below it was unreachable. See
		/// <see cref="TemplateID"/>.
		/// </para>
		/// </remarks>
		[Test]
		public void StartingEquipment_ResolvesAndIsResistedByArmor()
		{
			// Every armor piece a new character is created wearing, and the Armor each can roll.
			int worstCaseArmor = 0;
			List<string> pieces = new List<string>();
			foreach (int id in StartingEquipmentIds())
			{
				foreach (string guid in AssetDatabase.FindAssets("t:ArmorTemplate"))
				{
					ArmorTemplate armor = AssetDatabase.LoadAssetAtPath<ArmorTemplate>(
						AssetDatabase.GUIDToAssetPath(guid));
					if (armor == null || TemplateID(armor) != id || armor.ArmorBonus == null)
					{
						continue;
					}
					worstCaseArmor += armor.ArmorBonus.MaxValue;
					pieces.Add($"{armor.name}({armor.ArmorBonus.MaxValue})");
				}
			}
			Assert.That(pieces, Is.Not.Empty, "The starting equipment must resolve to real items.");

			AbilityTemplate punch = AssetDatabase.LoadAssetAtPath<AbilityTemplate>(
				"Assets/Templates/Entity/Abilities/Types/Punch.asset");
			Assert.That(punch, Is.Not.Null);
			int punchDamage = PhysicalDamageOf(punch);
			Assert.That(punchDamage, Is.GreaterThan(0),
				"Punch must deal physical damage, and it must be the damage type Armor resists — "
				+ "PhysicalDamageOf only counts damage whose Resistance is Armor, so a zero here means "
				+ "the two have come unhooked and armor stopped applying to melee entirely.");

			/* Recorded, not asserted against a ceiling. If someone later wants the starting fight to
			 * land damage again, this is the arithmetic to read: flat reduction, floor of zero. */
			TestContext.WriteLine(
				$"MEASURE startingArmorBestRolls = {worstCaseArmor} from {string.Join(", ", pieces)}");
			TestContext.WriteLine($"MEASURE punchPhysicalDamage = {punchDamage}");
		}

		/// <summary>
		/// The cache ID a template will have at runtime, derived the way the cache derives it.
		/// </summary>
		/// <remarks>
		/// <c>CachedScriptableObject.ID</c> is assigned by <c>AddToCache</c>, which only the client
		/// and server boot paths call as they walk the addressables. Nothing calls it in EditMode, so
		/// a template loaded through <c>AssetDatabase</c> reports ID 0 — and comparing an authored id
		/// against 0 matched nothing, which is why this test saw no starting equipment at all rather
		/// than the wrong starting equipment. <c>RaceRegistry</c> and <c>ModifierRegistry</c> document
		/// the same trap and derive the id the same way; <c>TemplateReferenceDrawer</c> does it too,
		/// which is how the editor's own template pickers resolve a reference.
		/// <para>
		/// The real ID is preferred when it is present, so this keeps working if a future harness
		/// does populate the cache.
		/// </para>
		/// </remarks>
		/// <param name="template">The template to identify.</param>
		/// <returns>The template's runtime cache ID.</returns>
		private static int TemplateID(ScriptableObject template)
		{
			if (template is ICachedObject cached && cached.ID != 0)
			{
				return cached.ID;
			}
			return (template.GetType().Name + template.name).GetDeterministicHashCode();
		}

		/// <summary>The equipment ids a new character is created with, from the login server config.</summary>
		private static IEnumerable<int> StartingEquipmentIds()
		{
			string text = System.IO.File.ReadAllText(
				"Assets/Prefabs/Server/LoginServer/CharacterCreateSystem.asset");
			System.Text.RegularExpressions.Match m =
				System.Text.RegularExpressions.Regex.Match(text, @"startingEquipmentIDs: ([0-9a-fA-F]*)");
			Assert.That(m.Success, Is.True, "startingEquipmentIDs must be present in the login server config.");
			string hex = m.Groups[1].Value;
			byte[] bytes = new byte[hex.Length / 2];
			for (int i = 0; i < bytes.Length; ++i)
			{
				bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
			}
			for (int i = 0; i + 4 <= bytes.Length; i += 4)
			{
				yield return BitConverter.ToInt32(bytes, i);
			}
		}

		/// <summary>The physical damage an ability's hit events apply, summed.</summary>
		private static int PhysicalDamageOf(AbilityTemplate template)
		{
			int total = 0;
			if (template.OnHitEvents == null)
			{
				return 0;
			}
			foreach (AbilityEvent hitEvent in template.OnHitEvents)
			{
				if (hitEvent?.OnConditionsMetActions == null)
				{
					continue;
				}
				foreach (FishMMO.Shared.Core.BaseAction action in hitEvent.OnConditionsMetActions)
				{
					if (!(action is ApplyDamageAction damage) ||
						damage.DamageAttributeTemplate == null ||
						damage.DamageAttributeTemplate.Resistance == null ||
						damage.DamageAttributeTemplate.Resistance.name != "Armor")
					{
						continue;
					}
					object value = damage.DamageValue;
					FieldInfo amount = value?.GetType().GetField("Amount");
					if (amount != null)
					{
						total += (int)amount.GetValue(value);
					}
				}
			}
			return total;
		}
	}
}
