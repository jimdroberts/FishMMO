using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Asserts that the shipped personality assets actually lean the way their names promise, now
	/// that leaning is the only thing steering an archetype toward the right half of its spellbook.
	/// </summary>
	/// <remarks>
	/// These weights replaced per-archetype ability ID lists. That removed a whole class of drift,
	/// but it moved the archetype's character into six numbers on an asset — and a number left at
	/// its default produces a healer that treats its heals exactly like its nukes, spawns, ticks,
	/// and looks fine. Nothing in the compiler can catch that, so it is caught here.
	/// </remarks>
	[TestFixture]
	public class AIPersonalityIntentAssetTests
	{
		/// <summary>Folder the shipped personality assets live in.</summary>
		private const string PERSONALITY_FOLDER = "Assets/Templates/Entity/NPCs/AI/Personalities";

		/// <summary>Every personality asset in the project, loaded once.</summary>
		private static List<AICombatPersonality> personalities;

		/// <summary>
		/// Loads every <see cref="AICombatPersonality"/> under the personality folder.
		/// </summary>
		[OneTimeSetUp]
		public void LoadPersonalities()
		{
			personalities = new List<AICombatPersonality>();

			foreach (string guid in AssetDatabase.FindAssets("t:AICombatPersonality", new[] { PERSONALITY_FOLDER }))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				AICombatPersonality personality = AssetDatabase.LoadAssetAtPath<AICombatPersonality>(path);
				if (personality != null)
				{
					personalities.Add(personality);
				}
			}
		}

		/// <summary>
		/// Finds one personality by asset name, failing the test if it is missing.
		/// </summary>
		/// <param name="name">The asset name, without extension.</param>
		/// <returns>The personality.</returns>
		private static AICombatPersonality Require(string name)
		{
			foreach (AICombatPersonality personality in personalities)
			{
				if (personality.name == name)
				{
					return personality;
				}
			}

			Assert.Fail($"Personality asset '{name}' was not found under {PERSONALITY_FOLDER}.");
			return null;
		}

		/// <summary>
		/// A healer must want to heal more than it wants to attack, or it will spend its casts on
		/// the enemy while an ally bleeds out.
		/// </summary>
		[Test]
		public void HealerPersonality_FavoursHealingOverDamage()
		{
			AICombatPersonality healer = Require("Healer Personality");

			Assert.Greater(healer.HealWeight, healer.DamageWeight,
				"A healer must weigh healing above damage.");
			Assert.Greater(healer.HealWeight, 1f,
				"A healer with a neutral heal weight is not a healer.");
		}

		/// <summary>
		/// A defender must lead with threat. This is the weight that replaced its taunt ID list.
		/// </summary>
		[Test]
		public void DefenderPersonality_FavoursThreatGeneration()
		{
			AICombatPersonality defender = Require("Defender Personality");

			Assert.Greater(defender.ThreatWeight, defender.DamageWeight,
				"A defender must weigh threat above raw damage.");
		}

		/// <summary>
		/// A crowd controller must reach for control before damage; that preference is the entire
		/// archetype.
		/// </summary>
		[Test]
		public void CrowdControllerPersonality_FavoursControlOverDamage()
		{
			AICombatPersonality controller = Require("Crowd Controller Personality");

			Assert.Greater(controller.ControlWeight, controller.DamageWeight,
				"A crowd controller must weigh control above damage.");
		}

		/// <summary>
		/// A raging enemy must want damage above everything, including its own survival.
		/// </summary>
		[Test]
		public void RagingPersonality_FavoursDamageAboveAll()
		{
			AICombatPersonality raging = Require("Raging Personality");

			Assert.Greater(raging.DamageWeight, raging.HealWeight,
				"A raging enemy does not stop to heal.");
			Assert.Greater(raging.DamageWeight, raging.ControlWeight,
				"A raging enemy hits rather than controls.");
		}

		/// <summary>
		/// No asset may carry a zero weight. Zero does not mean "deprioritise" — it multiplies the
		/// ability's score to nothing, so the NPC never uses that half of its spellbook at all,
		/// silently.
		/// </summary>
		[Test]
		public void NoPersonality_ZeroesAnIntentWeight()
		{
			foreach (AICombatPersonality personality in personalities)
			{
				Assert.Greater(personality.DamageWeight, 0f, $"{personality.name}: DamageWeight is zero.");
				Assert.Greater(personality.HealWeight, 0f, $"{personality.name}: HealWeight is zero.");
				Assert.Greater(personality.ControlWeight, 0f, $"{personality.name}: ControlWeight is zero.");
				Assert.Greater(personality.DebuffWeight, 0f, $"{personality.name}: DebuffWeight is zero.");
				Assert.Greater(personality.BuffWeight, 0f, $"{personality.name}: BuffWeight is zero.");
				Assert.Greater(personality.ThreatWeight, 0f, $"{personality.name}: ThreatWeight is zero.");
			}
		}

		/// <summary>
		/// Guards against the whole set being shipped at its defaults, which would compile, load,
		/// pass every other test here, and leave every archetype behaving identically.
		/// </summary>
		[Test]
		public void PersonalitySet_ActuallyDifferentiatesArchetypes()
		{
			Assert.IsNotEmpty(personalities, "No personality assets were found.");

			bool anyBiased = false;
			foreach (AICombatPersonality personality in personalities)
			{
				if (!Mathf.Approximately(personality.DamageWeight, 1f) ||
					!Mathf.Approximately(personality.HealWeight, 1f) ||
					!Mathf.Approximately(personality.ControlWeight, 1f) ||
					!Mathf.Approximately(personality.DebuffWeight, 1f) ||
					!Mathf.Approximately(personality.BuffWeight, 1f) ||
					!Mathf.Approximately(personality.ThreatWeight, 1f))
				{
					anyBiased = true;
					break;
				}
			}

			Assert.IsTrue(anyBiased,
				"Every personality carries neutral intent weights — archetypes will not differ in what they cast.");
		}
	}
}
