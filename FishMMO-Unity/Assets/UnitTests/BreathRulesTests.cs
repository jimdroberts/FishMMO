using FishMMO.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Breath and drowning as Jim set them (2026-10-05): a meter that drains under water and fills in about three
	/// seconds out of it; a flat tenth of maximum health a second once it is gone; lava's 50 fire a second; fire races
	/// immune to fire, aquatic races breathing water.
	/// </summary>
	[TestFixture]
	public class BreathRulesTests
	{
		[Test]
		public void HoldingItDrainsASecondASecondAndStopsAtEmpty()
		{
			Assert.That(BreathRules.Step(30f, 30f, holding: true, 1f), Is.EqualTo(29f));
			Assert.That(BreathRules.Step(0.5f, 30f, holding: true, 1f), Is.EqualTo(0f));
		}

		[Test]
		public void OutOfTheWaterItFillsInThreeSeconds()
		{
			float breath = 0f;
			for (int tick = 0; tick < 90; tick++)
			{
				breath = BreathRules.Step(breath, 30f, holding: false, 1f / 30f);
			}
			Assert.That(breath, Is.EqualTo(30f).Within(1e-3f), "full after three seconds");
			Assert.That(BreathRules.Step(30f, 30f, holding: false, 1f), Is.EqualTo(30f), "and never past full");
		}

		[Test]
		public void DrowningIsAFlatTenthOfMaximumHealth()
		{
			Assert.That(BreathRules.DrowningDamage(1000), Is.EqualTo(100));
			Assert.That(BreathRules.DrowningDamage(95), Is.EqualTo(10), "rounded up");
			Assert.That(BreathRules.DrowningDamage(3), Is.EqualTo(1), "never nothing");
		}

		[Test]
		public void ThePulseLandsOnTheFirstTickOfEachSecond()
		{
			const double tickDelta = 1.0 / 30.0;
			Assert.That(BreathRules.IsPulseTick(0u, tickDelta), Is.True);
			Assert.That(BreathRules.IsPulseTick(30u, tickDelta), Is.True);
			Assert.That(BreathRules.IsPulseTick(9000u, tickDelta), Is.True);
			for (uint tick = 1; tick < 30; tick++)
			{
				Assert.That(BreathRules.IsPulseTick(tick, tickDelta), Is.False, $"tick {tick}");
			}
		}

		[Test]
		public void ARaceIsImmuneOnlyToWhatItLists()
		{
			var race = ScriptableObject.CreateInstance<RaceTemplate>();
			var fire = ScriptableObject.CreateInstance<DamageAttributeTemplate>();
			var frost = ScriptableObject.CreateInstance<DamageAttributeTemplate>();
			try
			{
				race.DamageImmunities.Add(fire);
				Assert.That(race.IsImmuneTo(fire), Is.True);
				Assert.That(race.IsImmuneTo(frost), Is.False);
				Assert.That(race.IsImmuneTo(null), Is.False);
			}
			finally
			{
				Object.DestroyImmediate(race);
				Object.DestroyImmediate(fire);
				Object.DestroyImmediate(frost);
			}
		}

		[TestCase("Cinderling")]
		[TestCase("Fire Elemental")]
		[TestCase("Lava Elemental")]
		[TestCase("Magma Hound")]
		[TestCase("Phoenix")]
		[TestCase("Salamander")]
		public void FireRacesAreImmuneToFire(string name)
		{
			var race = AssetDatabase.LoadAssetAtPath<RaceTemplate>($"Assets/Templates/Entity/Races/Elemental/{name}.asset");
			var fire = AssetDatabase.LoadAssetAtPath<DamageAttributeTemplate>("Assets/Templates/Entity/CharacterAttributes/Damage/Fire Damage.asset");
			Assert.That(race, Is.Not.Null, name);
			Assert.That(fire, Is.Not.Null);
			Assert.That(race.IsImmuneTo(fire), Is.True, $"{name} swims in lava (Jim: an under-lava city)");
		}

		[Test]
		public void AquaticRacesBreatheWater()
		{
			string[] guids = AssetDatabase.FindAssets("t:RaceTemplate", new[] { "Assets/Templates/Entity/Races/Aquatic" });
			Assert.That(guids, Is.Not.Empty);
			foreach (string guid in guids)
			{
				var race = AssetDatabase.LoadAssetAtPath<RaceTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				Assert.That(race.BreathesWater, Is.True, race.name);
			}
		}

		[Test]
		public void DrowningIsADamageTypeNoResistanceStandsAgainst()
		{
			var drowning = AssetDatabase.LoadAssetAtPath<DamageAttributeTemplate>("Assets/Templates/Entity/CharacterAttributes/Damage/Drowning Damage.asset");
			Assert.That(drowning, Is.Not.Null, "the breath controller resolves it by this name");
			Assert.That(drowning.Resistance, Is.Null);
		}
	}
}
