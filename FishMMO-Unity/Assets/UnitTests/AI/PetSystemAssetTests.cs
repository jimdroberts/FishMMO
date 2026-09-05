using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.UnitTests.AI
{
	/// <summary>
	/// Asserts the pet-side invariants that the pet system depends on but nothing enforces at
	/// compile time.
	/// </summary>
	/// <remarks>
	/// Pet behaviour is spread across the prefab, the archetype asset, the summoning template and
	/// the shared attacking state. Every one of the bugs found in the pet audit was a mismatch
	/// between two of those, not a mistake inside any one of them.
	/// </remarks>
	[TestFixture]
	public class PetSystemAssetTests
	{
		/// <summary>Every pet archetype in the project.</summary>
		private static List<AIArchetypeTemplate> petArchetypes;

		/// <summary>
		/// Loads the pet archetypes.
		/// </summary>
		/// <summary>
		/// Assigns the template IDs that the runtime asset loader normally assigns.
		/// </summary>
		/// <remarks>
		/// <see cref="CachedScriptableObject{T}.ID"/> has a private setter and is only ever written
		/// inside <c>AddToCache</c>, which the addressables bootstrap calls at runtime. Under
		/// EditMode nothing calls it, so every template's ID sits at 0 — and
		/// <see cref="PetPrefabs_HaveAnAttributeDatabaseWithHealth"/> compares those IDs against the
		/// controller's <c>HealthResourceTemplateID</c>, which is a real deterministic hash. The
		/// comparison could therefore never succeed for any pet, no matter how the prefab was
		/// authored; the test was reporting a content fault that did not exist.
		///
		/// Registering them here reproduces what production does. The ID is
		/// <c>(typeName + assetName).GetDeterministicHashCode()</c>, so the values this produces are
		/// the same ones a running game would.
		/// </remarks>
		[OneTimeSetUp]
		public void RegisterAttributeTemplateIds()
		{
			foreach (string guid in AssetDatabase.FindAssets("t:CharacterAttributeTemplate"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				CharacterAttributeTemplate attribute = AssetDatabase.LoadAssetAtPath<CharacterAttributeTemplate>(path);
				if (attribute != null)
				{
					attribute.AddToCache(attribute.name);
				}
			}
		}

		[OneTimeSetUp]
		public void LoadPetArchetypes()
		{
			petArchetypes = new List<AIArchetypeTemplate>();

			foreach (string guid in AssetDatabase.FindAssets("t:AIArchetypeTemplate"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				AIArchetypeTemplate archetype = AssetDatabase.LoadAssetAtPath<AIArchetypeTemplate>(path);
				if (archetype != null && archetype.name.StartsWith("Pet - "))
				{
					petArchetypes.Add(archetype);
				}
			}
		}

		[Test]
		public void Project_ShipsAtLeastOnePetArchetype()
		{
			Assert.IsNotEmpty(petArchetypes);
		}

		[Test]
		public void EveryPetArchetype_UsesAnAttackingStateThatKnowsAboutOwners()
		{
			/* Pet leash and disengage live on BaseAttackingState so that any archetype works for a
			 * pet. This asserts the pet archetypes actually opt into it — an owner leash of zero
			 * means the pet can chase something to the far side of the map. */
			foreach (AIArchetypeTemplate archetype in petArchetypes)
			{
				BaseAttackingState state = archetype.AttackingState as BaseAttackingState;
				Assert.IsNotNull(state, $"'{archetype.name}' has no attacking state.");
				Assert.Greater(state.OwnerLeashRange, 0f,
					$"'{archetype.name}' has no owner leash.");
			}
		}

		[Test]
		public void PetFollowStates_CanCatchARunningOwner()
		{
			foreach (AIArchetypeTemplate archetype in petArchetypes)
			{
				PetIdleState follow = archetype.IdleState as PetIdleState;
				Assert.IsNotNull(follow, $"'{archetype.name}' does not idle into a pet follow state.");

				Assert.Greater(follow.FollowDistance, 0f,
					$"'{follow.name}' has no follow distance, so the pet has no heel position.");

				Assert.Greater(follow.TeleportDistance, follow.FollowDistance,
					$"'{follow.name}' teleports at or inside its follow distance — the pet would " +
					"warp to its owner constantly instead of walking.");

				Assert.Greater(follow.AggressiveSweepRate, 0f,
					$"'{follow.name}' has a zero aggressive sweep rate, so an aggressive pet would " +
					"run a physics sweep on every single AI tick.");
			}
		}

		[Test]
		public void PetFollowStates_DoNotLeashToASpawnPoint()
		{
			foreach (AIArchetypeTemplate archetype in petArchetypes)
			{
				BaseAIState follow = archetype.IdleState;
				Assert.AreEqual(0f, follow.LeashUpdateRate,
					$"'{follow.name}' has spawn-point leashing enabled. A pet's anchor is its owner, " +
					"and the two mechanisms fight each other.");
			}
		}

		[Test]
		public void PetStance_DefaultsToTheSafestValue()
		{
			/* Both stance and order are plain enums whose zero value is what a default-initialised
			 * struct, a cleared byte on the wire, or an unset serialized field resolves to. Passive
			 * and Follow are the values where a pet does the least damage if something upstream
			 * fails to set them, so they must be the zero values.
			 *
			 * Deliberately not asserted by constructing a Pet: Pet and PetController each pull in
			 * a NetworkObject through RequireComponent, and instantiating them in EditMode trips
			 * FishNet's duplicate-NetworkObject guard. The property initialisers are ordinary C#
			 * and the enum contract below is the part that can silently regress. */
			Assert.AreEqual(0, (int)PetStance.Passive,
				"Passive must be the zero value so an unset stance never makes a pet pick fights.");
			Assert.AreEqual(0, (int)PetMovementOrder.Follow,
				"Follow must be the zero value so an unset order never strands a pet.");

			Assert.AreNotEqual(PetStance.Passive, PetStance.Defensive);
			Assert.AreNotEqual(PetStance.Passive, PetStance.Aggressive);
		}

		[Test]
		public void PetStance_IsByteBackedForTheSpawnPayload()
		{
			/* Pet.WritePayload / ReadPayload move these as a single unpacked byte. Widening the
			 * enum without touching the payload would desync every pet spawn. */
			Assert.AreEqual(typeof(byte), System.Enum.GetUnderlyingType(typeof(PetStance)));
			Assert.AreEqual(typeof(byte), System.Enum.GetUnderlyingType(typeof(PetMovementOrder)));

			foreach (PetStance stance in System.Enum.GetValues(typeof(PetStance)))
			{
				Assert.LessOrEqual((int)stance, byte.MaxValue);
			}
		}

		[Test]
		public void PetAbilityTemplates_DoNotListNullAbilities()
		{
			// A null entry silently reduces the pet's spellbook with no error anywhere.
			foreach (string guid in AssetDatabase.FindAssets("t:PetAbilityTemplate"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				PetAbilityTemplate template = AssetDatabase.LoadAssetAtPath<PetAbilityTemplate>(path);
				if (template == null || template.PetAbilities == null)
				{
					continue;
				}

				for (int i = 0; i < template.PetAbilities.Count; i++)
				{
					Assert.IsNotNull(template.PetAbilities[i],
						$"'{template.name}'.PetAbilities[{i}] is null.");
				}
			}
		}

		[Test]
		public void PetAbilityTemplates_HaveAPrefabToSpawn()
		{
			foreach (string guid in AssetDatabase.FindAssets("t:PetAbilityTemplate"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				PetAbilityTemplate template = AssetDatabase.LoadAssetAtPath<PetAbilityTemplate>(path);
				if (template == null)
				{
					continue;
				}

				Assert.IsNotNull(template.PetPrefab,
					$"'{template.name}' has no PetPrefab — summoning it is a silent no-op.");
			}
		}

		[Test]
		public void PetAbilityTemplates_SpawnPrefabsThatAreActuallyPets()
		{
			/* PetSystem.SpawnAndInitializePet pulls the prefab out of the object pool and then
			 * asks it for a Pet component. A prefab without one is returned to the pool and the
			 * summon fails silently — the player casts, pays the cost, and nothing appears. */
			foreach (PetAbilityTemplate template in LoadPetAbilityTemplates())
			{
				if (template.PetPrefab == null)
				{
					continue;
				}

				Assert.IsNotNull(template.PetPrefab.GetComponent<Pet>(),
					$"'{template.name}'.PetPrefab ('{template.PetPrefab.name}') has no Pet component, " +
					"so summoning it can only fail.");
			}
		}

		[Test]
		public void PetPrefabs_HaveNoMovementStatesToWanderOffInto()
		{
			/* A wander or patrol state joins the movement roll, and TransitionToRandomMovementState
			 * — which is where a pet lands whenever a fight ends — can pick it. The pet wanders off
			 * from the owner it belongs to, and no amount of following brings it back until the
			 * roll changes. The controller reads both slots from its archetype, so this pins the
			 * archetype each pet prefab actually names. */
			foreach (PetAbilityTemplate template in LoadPetAbilityTemplates())
			{
				if (template.PetPrefab == null)
				{
					continue;
				}

				AIController controller = template.PetPrefab.GetComponent<AIController>();
				if (controller == null)
				{
					continue;
				}

				BaseAIState wander = controller.WanderState;
				BaseAIState patrol = controller.PatrolState;

				Assert.IsNull(wander,
					$"'{template.PetPrefab.name}' resolves a wander state, so it can drift away from its owner.");
				Assert.IsNull(patrol,
					$"'{template.PetPrefab.name}' resolves a patrol state, so it can drift away from its owner.");
			}
		}

		[Test]
		public void PetPrefabs_ResolveAPetFollowStateAndAnAttackingState()
		{
			foreach (PetAbilityTemplate template in LoadPetAbilityTemplates())
			{
				if (template.PetPrefab == null)
				{
					continue;
				}

				AIController controller = template.PetPrefab.GetComponent<AIController>();
				Assert.IsNotNull(controller,
					$"'{template.PetPrefab.name}' has no AIController, so it has no brain at all.");

				Assert.IsNotNull(controller.Archetype,
					$"'{template.PetPrefab.name}' has no AI archetype, so its controller has no states at all.");

				BaseAIState idle = controller.IdleState;
				BaseAIState initial = controller.InitialState;
				BaseAIState attacking = controller.AttackingState;

				Assert.IsInstanceOf<PetIdleState>(idle,
					$"'{template.PetPrefab.name}' does not resolve a PetIdleState, so it will never follow its owner.");
				Assert.IsInstanceOf<PetIdleState>(initial,
					$"'{template.PetPrefab.name}' does not START in a PetIdleState, so a freshly summoned pet stands still.");
				Assert.IsNotNull(attacking,
					$"'{template.PetPrefab.name}' resolves no attacking state, so it cannot fight even when ordered to.");
			}
		}

		[Test]
		public void PetPrefabs_HaveAnAttributeDatabaseWithHealth()
		{
			/* Pet persistence is built on the attribute controller: the pet's health is what
			 * decides whether the pet row is written as still out, and the restore path writes
			 * saved values back into this controller's dictionaries. A prefab with no attribute
			 * database populates neither dictionary, so AddNPCAttributes is a no-op, the pet has
			 * no health to lose, and every save records it as dismissed. */
			foreach (PetAbilityTemplate template in LoadPetAbilityTemplates())
			{
				if (template.PetPrefab == null)
				{
					continue;
				}

				CharacterAttributeController attributeController = template.PetPrefab.GetComponent<CharacterAttributeController>();
				Assert.IsNotNull(attributeController,
					$"'{template.PetPrefab.name}' has no CharacterAttributeController.");
				Assert.IsNotNull(attributeController.CharacterAttributeDatabase,
					$"'{template.PetPrefab.name}' has no attribute database, so it spawns with no attributes at all.");

				bool hasHealth = false;
				foreach (CharacterAttributeTemplate attribute in attributeController.CharacterAttributeDatabase.Attributes)
				{
					if (attribute != null &&
						attribute.IsResourceAttribute &&
						attribute.ID == attributeController.HealthResourceTemplateID)
					{
						hasHealth = true;
						break;
					}
				}

				Assert.IsTrue(hasHealth,
					$"'{template.PetPrefab.name}' resolves no health resource attribute, so it cannot be damaged, " +
					"killed, or saved as still summoned.");
			}
		}

		/// <summary>
		/// Every pet summoning template in the project.
		/// </summary>
		private static List<PetAbilityTemplate> LoadPetAbilityTemplates()
		{
			List<PetAbilityTemplate> templates = new List<PetAbilityTemplate>();

			foreach (string guid in AssetDatabase.FindAssets("t:PetAbilityTemplate"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				PetAbilityTemplate template = AssetDatabase.LoadAssetAtPath<PetAbilityTemplate>(path);
				if (template != null)
				{
					templates.Add(template);
				}
			}

			return templates;
		}
	}
}
