using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Proofs that an NPC does not flip between attacking and chasing when its target sits on the
	/// edge of its ability range.
	/// </summary>
	/// <remarks>
	/// Each flip toggles <c>NavMeshAgent.isStopped</c>, and because the agent has acceleration and
	/// braking it never reaches speed in either direction — the NPC visibly shudders in place
	/// instead of either fighting or chasing. A bare <c>distance &lt;= range</c> comparison
	/// produces exactly that against any target that strafes.
	/// </remarks>
	[TestFixture]
	public class AICombatHysteresisTests
	{
		/// <summary>
		/// Builds a ranged context sitting at a given distance with a usable 10 m ability.
		/// </summary>
		/// <param name="distance">Distance to the target.</param>
		/// <param name="wasAttacking">Whether the NPC attacked on the previous tick.</param>
		/// <returns>A populated context.</returns>
		private static AICombatContext AtRange(float distance, bool wasAttacking)
		{
			AICombatContext context = default;
			context.Distance = distance;
			context.PreferredDistance = 15f;
			context.MinComfortDistance = 0f;
			context.EmergencyRetreatThreshold = 0.5f;
			context.MeleeReach = 1f;
			context.HealthPercent = 1f;
			context.HasUsableAbility = true;
			context.AbilityRange = 10f;
			context.WasAttacking = wasAttacking;
			return context;
		}

		[Test]
		public void NotYetAttacking_MustBeInsideRangeToStart()
		{
			Assert.AreEqual(AICombatIntent.CloseDistance,
				AICombatDecision.Plan(AtRange(10.5f, wasAttacking: false)).Intent,
				"An NPC outside its range must close, not attack from beyond it.");
		}

		[Test]
		public void AlreadyAttacking_ToleratesSmallDrift()
		{
			Assert.AreEqual(AICombatIntent.Attack,
				AICombatDecision.Plan(AtRange(10.5f, wasAttacking: true)).Intent,
				"A target strafing a half-metre past range must not restart the chase.");
		}

		[Test]
		public void AlreadyAttacking_StillGivesUpOnRealDistance()
		{
			// Hysteresis is a tolerance, not a licence to attack from anywhere.
			Assert.AreEqual(AICombatIntent.CloseDistance,
				AICombatDecision.Plan(AtRange(14f, wasAttacking: true)).Intent);
		}

		[Test]
		public void TheHysteresisBandIsAsymmetric()
		{
			/* The whole mechanism: one distance must produce different intents depending on what
			 * the NPC did last tick. If both answers agree, there is no band and the oscillation
			 * is back. */
			float boundary = 10f * ((1f + AICombatDecision.RANGE_HYSTERESIS) * 0.5f);

			AICombatIntent fresh = AICombatDecision.Plan(AtRange(boundary, wasAttacking: false)).Intent;
			AICombatIntent engaged = AICombatDecision.Plan(AtRange(boundary, wasAttacking: true)).Intent;

			Assert.AreEqual(AICombatIntent.CloseDistance, fresh);
			Assert.AreEqual(AICombatIntent.Attack, engaged);
		}

		[Test]
		public void SweepingThroughTheBoundary_SettlesInsteadOfOscillating()
		{
			/* Walks a target slowly outward across the range boundary and asserts the intent
			 * changes exactly once. Without hysteresis this alternates as the distance jitters. */
			bool wasAttacking = true;
			int transitions = 0;
			AICombatIntent previous = AICombatIntent.Attack;

			for (float d = 9.0f; d <= 12.0f; d += 0.05f)
			{
				AICombatContext context = AtRange(d, wasAttacking);
				AICombatIntent intent = AICombatDecision.Plan(context).Intent;

				if (intent != previous)
				{
					transitions++;
					previous = intent;
				}

				wasAttacking = intent == AICombatIntent.Attack || intent == AICombatIntent.HoldPosition;
			}

			Assert.AreEqual(1, transitions,
				"Crossing the range boundary once must change the intent once.");
		}

		[Test]
		public void HoldPosition_CountsAsEngagedForTheNextTick()
		{
			/* An NPC with everything on cooldown holds its spacing. That is not "moving", so the
			 * next tick must still get the wider tolerance — otherwise the NPC starts jittering
			 * the moment its abilities go on cooldown. */
			AICombatContext context = AtRange(15f, wasAttacking: true);
			context.HasUsableAbility = false;

			Assert.AreEqual(AICombatIntent.HoldPosition, AICombatDecision.Plan(context).Intent);
		}

		[Test]
		public void HoldBand_IsWiderOnceEngaged()
		{
			// Same asymmetry applied to the no-ability spacing band.
			float justOutside = 15f * AICombatDecision.ENGAGE_SLACK * 1.05f;

			AICombatContext fresh = AtRange(justOutside, wasAttacking: false);
			fresh.HasUsableAbility = false;

			AICombatContext engaged = AtRange(justOutside, wasAttacking: true);
			engaged.HasUsableAbility = false;

			Assert.AreEqual(AICombatIntent.CloseDistance, AICombatDecision.Plan(fresh).Intent);
			Assert.AreEqual(AICombatIntent.HoldPosition, AICombatDecision.Plan(engaged).Intent);
		}

		[Test]
		public void HysteresisIsAMeaningfulMargin()
		{
			Assert.Greater(AICombatDecision.RANGE_HYSTERESIS, 1f,
				"A factor of 1 or less is no band at all.");
			Assert.Less(AICombatDecision.RANGE_HYSTERESIS, 1.5f,
				"Too wide and NPCs visibly attack from outside their stated range.");
		}
	}

	/// <summary>
	/// Asserts that the shipped archetypes actually opt into distance-based AI throttling.
	/// </summary>
	/// <remarks>
	/// The LOD system was fully implemented but no asset referenced it, so every NPC ran the full
	/// pipeline — enemy sweep, behaviour tree, boss script, state machine, virtual camera, threat
	/// decay — on every tick regardless of whether a player was anywhere near it. A LOD asset that
	/// nothing points at costs exactly as much as not having one.
	/// </remarks>
	[TestFixture]
	public class AILodConfigurationTests
	{
		private const string ARCHETYPE_FOLDER = "Assets/Templates/Entity/NPCs/AI/Archetypes";

		/// <summary>Every archetype in the project.</summary>
		private static List<AIArchetypeTemplate> archetypes;

		/// <summary>
		/// Loads the archetypes once.
		/// </summary>
		[OneTimeSetUp]
		public void Load()
		{
			archetypes = new List<AIArchetypeTemplate>();

			foreach (string guid in AssetDatabase.FindAssets("t:AIArchetypeTemplate", new[] { ARCHETYPE_FOLDER }))
			{
				AIArchetypeTemplate archetype =
					AssetDatabase.LoadAssetAtPath<AIArchetypeTemplate>(AssetDatabase.GUIDToAssetPath(guid));
				if (archetype != null)
				{
					archetypes.Add(archetype);
				}
			}
		}

		[Test]
		public void EveryArchetype_HasLodSettings()
		{
			foreach (AIArchetypeTemplate archetype in archetypes)
			{
				Assert.IsNotNull(archetype.LodSettings,
					$"'{archetype.name}' has no LOD settings, so every NPC using it runs the full " +
					"AI pipeline forever regardless of whether a player is nearby.");
			}
		}

		[Test]
		public void LodTiers_AreOrderedOutward()
		{
			foreach (AIArchetypeTemplate archetype in archetypes)
			{
				AILodSettings lod = archetype.LodSettings;
				if (lod == null) continue;

				Assert.Less(lod.ActiveDistanceSqr, lod.NearbyDistanceSqr,
					$"'{lod.name}': the Active band is not inside the Nearby band, so tiers are unreachable.");
				Assert.Less(lod.NearbyDistanceSqr, lod.FarDistanceSqr,
					$"'{lod.name}': the Nearby band is not inside the Far band.");
			}
		}

		[Test]
		public void LodInterval_GetsCoarserWithDistance()
		{
			foreach (AIArchetypeTemplate archetype in archetypes)
			{
				AILodSettings lod = archetype.LodSettings;
				if (lod == null) continue;

				Assert.Less(lod.ActiveTickInterval, lod.NearbyTickInterval,
					$"'{lod.name}': distant NPCs must tick less often than close ones, or LOD saves nothing.");
				Assert.Less(lod.NearbyTickInterval, lod.FarTickInterval,
					$"'{lod.name}': the Far tier is not cheaper than the Nearby tier.");
				Assert.LessOrEqual(lod.FarTickInterval, lod.DormantTickInterval,
					$"'{lod.name}': a dormant NPC checks in more often than a far one.");
			}
		}

		[Test]
		public void LodInterval_IsNeverZero()
		{
			// The interval is used as a divisor; zero is a DivideByZeroException every tick.
			foreach (AIArchetypeTemplate archetype in archetypes)
			{
				AILodSettings lod = archetype.LodSettings;
				if (lod == null) continue;

				Assert.Greater(lod.ActiveTickInterval, 0, $"'{lod.name}'");
				Assert.Greater(lod.NearbyTickInterval, 0, $"'{lod.name}'");
				Assert.Greater(lod.FarTickInterval, 0, $"'{lod.name}'");
				Assert.Greater(lod.DormantTickInterval, 0, $"'{lod.name}'");
				Assert.Greater(lod.ReevaluateInterval, 0f, $"'{lod.name}'");
			}
		}

		[Test]
		public void LodTiers_ProduceUsableRatesAtTheDefaultBrainRate()
		{
			/* The intervals only mean something in combination with the AI tick rate, so assert
			 * the hertz they actually produce rather than the raw numbers. An Active tier slower
			 * than a couple of updates a second reads as an NPC with visibly delayed reactions. */
			const float defaultAiTickRate = 8f;

			foreach (AIArchetypeTemplate archetype in archetypes)
			{
				AILodSettings lod = archetype.LodSettings;
				if (lod == null) continue;

				float active = lod.GetTierHertz(AILodTier.Active, defaultAiTickRate);
				Assert.GreaterOrEqual(active, 2f,
					$"'{lod.name}': the Active tier runs at {active:F1} Hz, which reads as sluggish.");

				float dormant = lod.GetTierHertz(AILodTier.Dormant, defaultAiTickRate);
				Assert.Greater(dormant, 0f,
					$"'{lod.name}': a dormant NPC must still check whether a player has approached.");
				Assert.Less(dormant, active,
					$"'{lod.name}': dormant NPCs cost as much as active ones.");
			}
		}

		[Test]
		public void PetArchetypes_StayResponsive()
		{
			/* A pet is always beside its owner and always on screen, so it is the one thing that
			 * must not be throttled into looking sluggish. It should tick at least as often at the
			 * Nearby tier as a wild NPC does. */
			AILodSettings pet = null;
			AILodSettings enemy = null;

			foreach (AIArchetypeTemplate archetype in archetypes)
			{
				if (archetype.LodSettings == null) continue;

				if (archetype.name.StartsWith("Pet - ")) pet = archetype.LodSettings;
				else if (archetype.name.StartsWith("Enemy - ")) enemy = archetype.LodSettings;
			}

			Assume.That(pet, Is.Not.Null);
			Assume.That(enemy, Is.Not.Null);

			Assert.LessOrEqual(pet.NearbyTickInterval, enemy.NearbyTickInterval,
				"Pets are always on screen and must not be throttled harder than distant mobs.");
			Assert.GreaterOrEqual(pet.ActiveDistanceSqr, enemy.ActiveDistanceSqr,
				"A pet should stay on the Active tier further out than a wild NPC.");
		}
	}
}
