using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Asserts that the AI archetype assets shipped with the project actually behave the way
	/// their names promise.
	/// </summary>
	/// <remarks>
	/// <para>
	/// An archetype is data, so nothing about it is checked by the compiler. A "pathetic" enemy
	/// whose retreat threshold got left at zero, a caster whose comfort distance ended up larger
	/// than its preferred distance, or a variety state without <c>KeepsCombatTarget</c> all
	/// produce an NPC that spawns, ticks, and quietly misbehaves. These tests load the real
	/// assets from disk and assert the properties that make each archetype what it is.
	/// </para>
	/// <para>
	/// EditMode only — they use <see cref="AssetDatabase"/> to find the shipped assets.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AIArchetypeAssetTests
	{
		/// <summary>Folder the generated archetype assets live in.</summary>
		private const string ARCHETYPE_FOLDER = "Assets/Templates/Entity/NPCs/AI/Archetypes";

		/// <summary>Every archetype asset in the project, loaded once.</summary>
		private static List<AIArchetypeTemplate> archetypes;

		/// <summary>
		/// Loads every <see cref="AIArchetypeTemplate"/> under the archetype folder.
		/// </summary>
		[OneTimeSetUp]
		public void LoadArchetypes()
		{
			archetypes = new List<AIArchetypeTemplate>();

			string[] guids = AssetDatabase.FindAssets("t:AIArchetypeTemplate", new[] { ARCHETYPE_FOLDER });
			for (int i = 0; i < guids.Length; i++)
			{
				string path = AssetDatabase.GUIDToAssetPath(guids[i]);
				AIArchetypeTemplate archetype = AssetDatabase.LoadAssetAtPath<AIArchetypeTemplate>(path);
				if (archetype != null)
				{
					archetypes.Add(archetype);
				}
			}
		}

		/// <summary>
		/// Finds one archetype by asset name, failing the test if it is missing.
		/// </summary>
		/// <param name="name">The asset name, without extension.</param>
		/// <returns>The archetype.</returns>
		private static AIArchetypeTemplate Require(string name)
		{
			for (int i = 0; i < archetypes.Count; i++)
			{
				if (archetypes[i].name == name)
				{
					return archetypes[i];
				}
			}
			Assert.Fail($"Archetype asset '{name}' was not found under {ARCHETYPE_FOLDER}.");
			return null;
		}

		/// <summary>
		/// True for an archetype that is meant to fight. Civilians — merchants, bankers, trainers —
		/// are archetypes too, and deliberately have neither an attacking state nor a personality.
		/// </summary>
		/// <param name="archetype">The archetype to classify.</param>
		/// <returns>True unless the asset is a civilian brain.</returns>
		private static bool IsCombatant(AIArchetypeTemplate archetype)
		{
			return !archetype.name.StartsWith("Civilian - ");
		}

		/// <summary>
		/// Returns an archetype's attacking state as a <see cref="BaseAttackingState"/>.
		/// </summary>
		/// <param name="archetype">The archetype to read.</param>
		/// <returns>The attacking state.</returns>
		private static BaseAttackingState AttackState(AIArchetypeTemplate archetype)
		{
			BaseAttackingState state = archetype.AttackingState as BaseAttackingState;
			Assert.IsNotNull(state, $"'{archetype.name}' has no BaseAttackingState assigned.");
			return state;
		}

		// --- Coverage ----------------------------------------------------------------------

		[Test]
		public void Project_ShipsArchetypesForEveryRole()
		{
			// The roles a designer is expected to be able to reach for without writing code.
			string[] required =
			{
				"Enemy - Melee",
				"Enemy - Brute",
				"Enemy - Pathetic Critter",
				"Enemy - Raging Beast",
				"Enemy - Archer",
				"Enemy - Caster",
				"Enemy - Crowd Controller",
				"Enemy - Healer",
				"Enemy - Defender",
				"Enemy - Rogue",
				"Pet - Melee",
				"Pet - Archer",
				"Pet - Caster",
				"Pet - Healer",
				"Pet - Defender",
				"Pet - Rogue",
				"Civilian - Townsfolk",
			};

			for (int i = 0; i < required.Length; i++)
			{
				Require(required[i]);
			}
		}

		// --- Structural validity -----------------------------------------------------------

		[Test]
		public void EveryArchetype_IsInternallyConsistent()
		{
			/* AIArchetypeTemplate.Validate encodes the combinations that produce an NPC which
			 * spawns and then does nothing: a null initial state, a personality that flees with
			 * nowhere to flee to, a variety state that drops the combat target, a detection
			 * radius of zero. */
			List<string> problems = new List<string>();
			StringBuilder report = new StringBuilder();

			for (int i = 0; i < archetypes.Count; i++)
			{
				if (!archetypes[i].Validate(problems))
				{
					report.AppendLine(archetypes[i].name + ":");
					for (int p = 0; p < problems.Count; p++)
					{
						report.AppendLine("    " + problems[p]);
					}
				}
			}

			Assert.IsEmpty(report.ToString(), "Archetype assets have configuration problems:\n" + report);
		}

		[Test]
		public void EveryCombatArchetype_HasAnAttackingStateSoItCanFight()
		{
			for (int i = 0; i < archetypes.Count; i++)
			{
				if (!IsCombatant(archetypes[i]))
				{
					continue;
				}

				Assert.IsNotNull(archetypes[i].AttackingState,
					$"'{archetypes[i].name}' has no attacking state — an NPC using it can never fight.");
			}
		}

		[Test]
		public void EveryCombatArchetype_HasAPersonalitySoItsAbilityChoicesHaveCharacter()
		{
			for (int i = 0; i < archetypes.Count; i++)
			{
				if (!IsCombatant(archetypes[i]))
				{
					continue;
				}

				Assert.IsNotNull(archetypes[i].Personality,
					$"'{archetypes[i].name}' has no personality; it will score every ability identically.");
			}
		}

		[Test]
		public void CivilianArchetypes_CannotFightButStillIdleAndMove()
		{
			/* A merchant with an attacking state would sweep for enemies and chase the first
			 * hostile that walked past its stall. It still needs an initial and an idle state,
			 * or it spawns and never ticks. */
			int civilians = 0;
			for (int i = 0; i < archetypes.Count; i++)
			{
				AIArchetypeTemplate archetype = archetypes[i];
				if (IsCombatant(archetype))
				{
					continue;
				}
				civilians++;

				Assert.IsNull(archetype.AttackingState, $"'{archetype.name}' is a civilian and must not fight.");
				Assert.IsNotNull(archetype.InitialState, $"'{archetype.name}' has no initial state.");
				Assert.IsNotNull(archetype.IdleState, $"'{archetype.name}' has no idle state.");
				Assert.IsNotNull(archetype.LodSettings, $"'{archetype.name}' has no LOD profile, so a town full of them ticks at full rate for nobody.");
			}

			Assert.Greater(civilians, 0, "No civilian archetype ships; interactable NPCs have nothing to assign.");
		}

		// --- Prefab wiring -----------------------------------------------------------------

		[Test]
		public void EveryNPCPrefab_NamesAnArchetype()
		{
			/* The archetype is the ONLY AI wiring a prefab carries: every state, the personality,
			 * the rotation, the LOD profile and the threat tuning are read from it. A controller
			 * without one has no initial state, so the NPC spawns and never ticks — and nothing at
			 * compile time says so. */
			StringBuilder missing = new StringBuilder();
			int scanned = 0;

			foreach (GameObject root in NPCPrefabFactory.FindNPCPrefabs(includeLocal: true))
			{
				AIController controller = root.GetComponent<AIController>();
				if (controller == null)
				{
					continue;
				}

				scanned++;
				if (controller.Archetype == null)
				{
					missing.AppendLine("    " + AssetDatabase.GetAssetPath(root));
				}
			}

			Assert.Greater(scanned, 0, "No prefab with an AIController was found; the scan is broken.");
			Assert.IsEmpty(missing.ToString(), "NPC prefabs with an AIController but no archetype:\n" + missing);
		}

		// --- Named behaviour: the point of the exercise -------------------------------------

		[Test]
		public void PatheticCritter_RunsAwayWhenHurtAndFightsWhenHealthy()
		{
			AIArchetypeTemplate archetype = Require("Enemy - Pathetic Critter");
			AICombatPersonality personality = archetype.Personality;

			Assert.AreEqual(NPCCombatStyle.Pathetic, personality.Style);
			Assert.Greater(personality.EffectiveRetreatHealthThreshold, 0f,
				"A pathetic enemy with no effective retreat threshold never runs.");
			Assert.IsTrue(personality.ShouldRetreat(personality.EffectiveRetreatHealthThreshold));
			Assert.IsFalse(personality.ShouldRetreat(1f), "It should still fight while healthy.");

			Assert.IsNotNull(archetype.RetreatState,
				"It needs somewhere to flee to, or the flee decision cannot be carried out.");

			// And the decision layer agrees, using the archetype's own numbers.
			BaseAttackingState state = AttackState(archetype);
			AICombatContext hurt = BuildContext(state, personality, distance: 2f, healthPercent: 0.1f,
				canFlee: archetype.RetreatState != null);
			Assert.AreEqual(AICombatIntent.Flee, AICombatDecision.Plan(hurt).Intent);

			AICombatContext healthy = BuildContext(state, personality, distance: 2f, healthPercent: 1f,
				canFlee: archetype.RetreatState != null);
			Assert.AreNotEqual(AICombatIntent.Flee, AICombatDecision.Plan(healthy).Intent);
		}

		[Test]
		public void RagingBeast_NeverRunsAndCannotBeHeldWithThreat()
		{
			AIArchetypeTemplate archetype = Require("Enemy - Raging Beast");
			AICombatPersonality personality = archetype.Personality;

			Assert.AreEqual(NPCCombatStyle.Rampaging, personality.Style);
			Assert.IsTrue(personality.IsFearless);
			Assert.IsFalse(personality.ShouldRetreat(0.01f),
				"A raging beast must fight on at 1% health.");
			Assert.AreEqual(AITargetingMode.Random, personality.TargetingMode,
				"A raging beast must ignore the threat table.");
			Assert.Greater(personality.EffectiveRetargetChance, 0f,
				"A raging beast must actually re-roll onto new victims mid-fight.");

			BaseAttackingState state = AttackState(archetype);
			Assert.Greater(state.TargetReevaluationRate, 0f,
				"Re-targeting is driven by the re-evaluation timer; at 0 the rampage never fires.");

			AICombatContext nearlyDead = BuildContext(state, personality, distance: 2f,
				healthPercent: 0.01f, canFlee: true);
			Assert.AreNotEqual(AICombatIntent.Flee, AICombatDecision.Plan(nearlyDead).Intent);
		}

		[Test]
		public void DeterminedArchetypes_NeverRun()
		{
			string[] names = { "Enemy - Brute", "Enemy - Defender" };

			for (int i = 0; i < names.Length; i++)
			{
				AIArchetypeTemplate archetype = Require(names[i]);
				Assert.IsTrue(archetype.Personality.IsFearless,
					$"'{names[i]}' is meant to hold the line; its personality must be a fearless style.");
				Assert.IsFalse(archetype.Personality.ShouldRetreat(0.01f));
			}
		}

		[Test]
		public void RangedArchetypes_HoldRangeAndHaveSomewhereToBackAwayTo()
		{
			string[] names = { "Enemy - Archer", "Enemy - Caster", "Enemy - Crowd Controller", "Enemy - Healer" };

			for (int i = 0; i < names.Length; i++)
			{
				AIArchetypeTemplate archetype = Require(names[i]);
				BaseAttackingState state = AttackState(archetype);

				Assert.Greater(state.PreferredDistance, 0f,
					$"'{names[i]}' is a ranged archetype and must hold a working distance.");
				Assert.Greater(state.MinComfortDistance, 0f,
					$"'{names[i]}' must be unwilling to be meleed.");
				Assert.Less(state.MinComfortDistance, state.PreferredDistance,
					$"'{names[i]}' would sit permanently inside its own kiting band.");
				Assert.Greater(state.DetectionRadius, state.PreferredDistance,
					$"'{names[i]}' cannot detect an enemy at the range it wants to fight from.");
			}
		}

		[Test]
		public void MeleeArchetypes_CloseToReachAndNeverKite()
		{
			string[] names = { "Enemy - Melee", "Enemy - Brute", "Enemy - Defender", "Enemy - Rogue" };

			for (int i = 0; i < names.Length; i++)
			{
				BaseAttackingState state = AttackState(Require(names[i]));

				Assert.AreEqual(0f, state.PreferredDistance,
					$"'{names[i]}' is melee and must close all the way.");
				Assert.AreEqual(0f, state.MinComfortDistance,
					$"'{names[i]}' is melee and must never back away from its target.");
			}
		}

		[Test]
		public void HealerArchetypes_CanActuallySeeTheirAllies()
		{
			string[] names = { "Enemy - Healer", "Pet - Healer" };

			for (int i = 0; i < names.Length; i++)
			{
				HealerAttackingState state = AttackState(Require(names[i])) as HealerAttackingState;
				Assert.IsNotNull(state, $"'{names[i]}' must use a HealerAttackingState.");

				Assert.AreNotEqual(0, state.AllyLayers.value,
					$"'{names[i]}' has an empty ally layer mask — its ally sweep can never hit anything.");
				Assert.Greater(state.AllyScanRadius, 0f);
				Assert.Greater(state.HealThreshold, 0f,
					$"'{names[i]}' would never consider anybody injured enough to heal.");
				Assert.LessOrEqual(state.HealThreshold, 1f);
			}
		}

		[Test]
		public void DefenderArchetypes_BodyBlockAndResistBeingPulledOff()
		{
			string[] names = { "Enemy - Defender", "Pet - Defender" };

			for (int i = 0; i < names.Length; i++)
			{
				DefenderAttackingState state = AttackState(Require(names[i])) as DefenderAttackingState;
				Assert.IsNotNull(state, $"'{names[i]}' must use a DefenderAttackingState.");

				Assert.IsTrue(state.BodyBlock, $"'{names[i]}' is a defender and should interpose.");
				Assert.Greater(state.BlockStandoffDistance, 0f);
				Assert.GreaterOrEqual(state.AggressionSwitchThreshold, 100f,
					$"'{names[i]}' switches targets too easily to hold a pull.");
			}
		}

		[Test]
		public void RogueArchetypes_FlankWithinABoundedBudget()
		{
			string[] names = { "Enemy - Rogue", "Pet - Rogue" };

			for (int i = 0; i < names.Length; i++)
			{
				RogueAttackingState state = AttackState(Require(names[i])) as RogueAttackingState;
				Assert.IsNotNull(state, $"'{names[i]}' must use a RogueAttackingState.");

				Assert.Greater(state.MaxFlankSeconds, 0f,
					$"'{names[i]}' would circle forever without ever attacking.");
				Assert.Greater(state.FlankArcDegrees, 0f);
				Assert.Less(state.FlankArcDegrees, 180f,
					$"'{names[i]}' counts every position as flanked, so it never bothers circling.");
			}
		}

		// --- Pets --------------------------------------------------------------------------

		[Test]
		public void EveryPetArchetype_HeelsAtItsOwnerAndComesBack()
		{
			for (int i = 0; i < archetypes.Count; i++)
			{
				AIArchetypeTemplate archetype = archetypes[i];
				if (!archetype.name.StartsWith("Pet - "))
				{
					continue;
				}

				Assert.IsInstanceOf<PetIdleState>(archetype.IdleState,
					$"'{archetype.name}' must idle into a follow state, or the pet will not heel.");

				/* A pet's disengage path goes through TransitionToIdleState, so the initial and
				 * idle slots have to be the same follow state — otherwise a pet that finishes a
				 * fight lands somewhere that does not follow its owner. */
				Assert.AreSame(archetype.IdleState, archetype.InitialState,
					$"'{archetype.name}' must start in the same follow state it returns to.");

				BaseAttackingState state = AttackState(archetype);
				Assert.Greater(state.OwnerLeashRange, 0f,
					$"'{archetype.name}' has no owner leash — it can chase a target off the map.");

				Assert.AreEqual(0f, state.LeashUpdateRate,
					$"'{archetype.name}' should leash to its owner, not to a fixed spawn point.");
			}
		}

		[Test]
		public void PetArchetypes_DoNotWander()
		{
			for (int i = 0; i < archetypes.Count; i++)
			{
				AIArchetypeTemplate archetype = archetypes[i];
				if (!archetype.name.StartsWith("Pet - "))
				{
					continue;
				}

				/* TransitionToRandomMovementState picks from whichever of these are assigned. A
				 * pet with a wander state would drift away from the owner it belongs to. */
				Assert.IsNull(archetype.WanderState, $"'{archetype.name}' must not wander.");
				Assert.IsNull(archetype.PatrolState, $"'{archetype.name}' must not patrol.");
				Assert.IsNull(archetype.ReturnHomeState,
					$"'{archetype.name}' must not leash to a spawn point.");
			}
		}

		// --- Combat sub-states -------------------------------------------------------------

		[Test]
		public void EveryVarietyState_KeepsTheCombatTarget()
		{
			/* Entering a positioning state is a manoeuvre, not a disengage. A variety state
			 * without this flag has its target cleared by the attacking state's Exit and bails
			 * straight to idle, so every roll silently ends the fight. */
			for (int i = 0; i < archetypes.Count; i++)
			{
				BaseAttackingState state = archetypes[i].AttackingState as BaseAttackingState;
				if (state == null || state.VarietyStates == null)
				{
					continue;
				}

				for (int v = 0; v < state.VarietyStates.Count; v++)
				{
					BaseAIState variety = state.VarietyStates[v];
					Assert.IsNotNull(variety, $"'{state.name}' has a null variety state.");
					Assert.IsTrue(variety.KeepsCombatTarget,
						$"'{variety.name}', used by '{state.name}', drops the combat target on entry.");
				}

				if (state.EmergencyRetreatState != null)
				{
					Assert.IsTrue(state.EmergencyRetreatState.KeepsCombatTarget,
						$"'{state.EmergencyRetreatState.name}' needs the target to know which way to run.");
				}
			}
		}

		[Test]
		public void EveryRetreatState_KeepsTheCombatTarget()
		{
			for (int i = 0; i < archetypes.Count; i++)
			{
				BaseAIState retreat = archetypes[i].RetreatState;
				if (retreat == null)
				{
					continue;
				}

				Assert.IsTrue(retreat.KeepsCombatTarget,
					$"'{retreat.name}' computes its escape direction from the target, so it must keep it.");
			}
		}

		[Test]
		public void EveryState_CanSeeSomethingToFight()
		{
			for (int i = 0; i < archetypes.Count; i++)
			{
				BaseAttackingState state = archetypes[i].AttackingState as BaseAttackingState;
				if (state == null)
				{
					continue;
				}

				Assert.AreNotEqual(0, state.EnemyLayers.value,
					$"'{state.name}' has an empty enemy layer mask — its sweep can never hit anything.");
				Assert.Greater(state.DetectionRadius, 0f,
					$"'{state.name}' has a zero detection radius.");
			}
		}

		// --- Helper ------------------------------------------------------------------------

		/// <summary>
		/// Builds the same decision context the attacking state builds at runtime, from an
		/// archetype's own serialized numbers.
		/// </summary>
		/// <param name="state">The archetype's attacking state.</param>
		/// <param name="personality">The archetype's personality.</param>
		/// <param name="distance">Simulated distance to the target.</param>
		/// <param name="healthPercent">Simulated health fraction.</param>
		/// <param name="canFlee">Whether a retreat state is available.</param>
		/// <returns>A populated context.</returns>
		private static AICombatContext BuildContext(BaseAttackingState state,
			AICombatPersonality personality, float distance, float healthPercent, bool canFlee)
		{
			AICombatContext context = default;
			context.Distance = distance;
			context.PreferredDistance = state.PreferredDistance;
			context.MinComfortDistance = state.MinComfortDistance;
			context.EmergencyRetreatThreshold = state.EmergencyRetreatThreshold;
			context.MeleeReach = 1f;
			context.HealthPercent = healthPercent;
			context.FleeHealthThreshold = personality != null ? personality.EffectiveRetreatHealthThreshold : 0f;
			context.CanFlee = canFlee && personality != null;
			return context;
		}
	}
}
