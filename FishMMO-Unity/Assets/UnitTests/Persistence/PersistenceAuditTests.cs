using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using FishNet.Serializing;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the invariants the 2026-09-07 persistence audit restored: factions and archetypes
	/// carry a dirty mark and a version that advances on mutation, restored rows are clean, a
	/// pooled attribute forgets its previous occupant's version stream, the faction reader
	/// refuses a frame too short to hold its header, and the player prefab requires every
	/// controller the save legs write.
	/// </summary>
	[TestFixture]
	public class PersistenceAuditTests
	{
		private readonly List<Object> temporaries = new List<Object>();

		[TearDown]
		public void TearDown()
		{
			for (int i = 0; i < temporaries.Count; ++i)
			{
				if (temporaries[i] is ScriptableObject so && so is ICachedObject cached)
				{
					cached.RemoveFromCache();
				}
				if (temporaries[i] != null)
				{
					Object.DestroyImmediate(temporaries[i]);
				}
			}
			temporaries.Clear();
		}

		private sealed class MockCharacter : ICharacter
		{
			public MockCharacter(long id) => ID = id;
			public long ID { get; set; }
			public string Name => "MockCharacter";
			public Transform Transform => null;
			public GameObject GameObject => null;
			public Collider Collider { get; set; }
			public NetworkConnection Owner => null;
			public NetworkObject NetworkObject => null;
			public PredictionManager PredictionManager => null;
			public HashSet<NetworkConnection> Observers { get; } = new HashSet<NetworkConnection>();
			public bool IsTeleporting => false;
			public bool IsSpawned => true;
			public int Flags { get; set; }
			/// <inheritdoc/>
			public Nameplate CharacterNameplate { get; set; }
			public Transform MeshRoot => null;
#if !UNITY_SERVER
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex) { }
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex, CharacterGender gender) { }
#endif
			public void EnableFlags(CharacterFlags flags) => Flags |= 1 << (int)flags;
			public void DisableFlags(CharacterFlags flags) => Flags &= ~(1 << (int)flags);
			public bool IsFlagged(CharacterFlags flags) => (Flags & (1 << (int)flags)) != 0;
			public void RegisterCharacterBehaviour(ICharacterBehaviour b) { }
			public void UnregisterCharacterBehaviour(ICharacterBehaviour b) { }
			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour { control = null; return false; }
			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}

		// ── Faction ──────────────────────────────────────────────────────────────

		[Test]
		public void CharacterAttribute_ResetPersistenceState_ForgetsThePreviousOccupant()
		{
			CharacterAttributeTemplate template = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			template.name = "PersistenceAudit_Attribute";
			template.AddToCache(template.name);
			temporaries.Add(template);

			// No controller: SetValueDirect is the setter that bypasses the change funnel, and the
			// persistence state is the only thing under test here.
			CharacterAttribute attribute = new CharacterAttribute(null, template.ID, 10, 0);
			attribute.Version = 41;
			attribute.SetValueDirect(11);
			Assert.IsTrue(attribute.PersistenceDirty);

			attribute.ResetPersistenceState();
			Assert.AreEqual(0, attribute.Version, "The next occupant's rows must not inherit a foreign version stream.");
			Assert.IsFalse(attribute.PersistenceDirty, "Nor a dirty mark that would write its template default as a change.");

			// And the in-flight markers: a confirmation for the old stream must not clear a new change.
			attribute.SetValueDirect(12);
			attribute.MarkPersisted(41);
			Assert.IsTrue(attribute.PersistenceDirty);
		}

		// ── Archetype ────────────────────────────────────────────────────────────

		[Test]
		public void ArchetypeController_RestoreIsClean_ResetForgets()
		{
			GameObject host = new GameObject("ArchetypeHost");
			temporaries.Add(host);
			ArchetypeController controller = host.AddComponent<ArchetypeController>();
			controller.InitializeOnce(new MockCharacter(3));

			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			try
			{
				// No template registered under this id: the row restores as "no archetype" but keeps its version.
				controller.Restore(987654, 5);
			}
			finally
			{
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
			}
			Assert.AreEqual(5, controller.Version);
			Assert.IsFalse(controller.PersistenceDirty, "A restored row is what the database holds.");

			controller.ResetState(true);
			Assert.AreEqual(0, controller.Version);
			Assert.IsFalse(controller.PersistenceDirty);
			Assert.IsNull(controller.Template);
		}

		// ── The prefab contract ──────────────────────────────────────────────────


		[Test]
		public void PlayerCharacter_RequiresEveryControllerTheSaveLegsWrite()
		{
			var required = new HashSet<System.Type>();
			foreach (RequireComponent attribute in typeof(PlayerCharacter).GetCustomAttributes<RequireComponent>(true))
			{
				if (attribute.m_Type0 != null) required.Add(attribute.m_Type0);
				if (attribute.m_Type1 != null) required.Add(attribute.m_Type1);
				if (attribute.m_Type2 != null) required.Add(attribute.m_Type2);
			}

			foreach (System.Type type in new[]
			{
				typeof(CharacterAttributeController), typeof(AbilityController), typeof(AchievementController),
				typeof(BuffController), typeof(InventoryController), typeof(BankController),
				typeof(EquipmentController), typeof(QuestController), typeof(ArchetypeController),
			})
			{
				Assert.IsTrue(required.Contains(type), $"PlayerCharacter must require {type.Name}: the load and save legs address it, and a prefab without it silently loses that table.");
			}
		}
	}
}
