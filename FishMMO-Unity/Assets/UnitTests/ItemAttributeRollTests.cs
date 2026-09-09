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
	/// Item attribute ranges are authored INCLUSIVE, and starting gear must not out-scale the
	/// basic melee abilities.
	/// </summary>
	/// <remarks>
	/// Resistance is a 1:1 flat reduction by design, so a character's Armor is subtracted whole
	/// from every physical hit. Two things follow, and both are pinned here. Authored tiers are
	/// tight ranges at the small end, so an exclusive upper bound would collapse every one of them
	/// to its minimum; and the starting kit's total Armor has to leave something behind when the
	/// weakest shipped melee ability lands, or the starting enemy cannot damage a new character at
	/// all (reported 2026-09-07: orcs attacked and dealt nothing, because five starting armor
	/// pieces granted 5..10 Armor against a 5 damage Punch).
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

		[Test]
		public void StartingEquipment_LeavesTheWeakestMeleeAbilityAbleToDamage()
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
					if (armor == null || armor.ID != id || armor.ArmorBonus == null)
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
			Assert.That(punchDamage, Is.GreaterThan(0), "Punch must deal physical damage.");

			Assert.That(worstCaseArmor, Is.LessThan(punchDamage),
				$"Starting Armor ({worstCaseArmor} at best rolls from {string.Join(", ", pieces)}) must stay "
				+ $"below Punch's {punchDamage} physical damage. Resistance is a 1:1 reduction, so armor "
				+ "at or above the damage makes a new character immune to the starting enemy.");
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
