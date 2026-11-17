using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using UnityEditor;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The Archetypes module (Phase 3, 2026-11-17): the archetype is a character part, saved through its participant;
	/// the template stays in the core. The controller tests moved here from ObserverPayloadShapingTests and
	/// PersistenceAuditTests with the controller.
	/// </summary>
	[TestFixture]
	public class ArchetypesModuleTests
	{
		private readonly List<Object> temporaries = new List<Object>();

		private static readonly Dictionary<ICharacter, int> TriggerDispatches = new Dictionary<ICharacter, int>();

		[SetUp]
		public void CountTriggerDispatches()
		{
			TriggerDispatches.Clear();
			Trigger.DispatchObserved = (triggers, eventData) =>
			{
				if (eventData?.Initiator != null)
				{
					TriggerDispatches.TryGetValue(eventData.Initiator, out int count);
					TriggerDispatches[eventData.Initiator] = count + 1;
				}
			};
		}

		[TearDown]
		public void TearDown()
		{
			Trigger.DispatchObserved = null;
			TriggerDispatches.Clear();
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

		private sealed class RecordingCharacter : ICharacter
		{
			public RecordingCharacter(long id) => ID = id;
			public int InvokeCount => TriggerDispatches.TryGetValue(this, out int count) ? count : 0;
			public long ID { get; set; }
			public string Name => "RecordingCharacter";
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
		}

		private ArchetypeTemplate NewArchetype(string name)
		{
			ArchetypeTemplate template = ScriptableObject.CreateInstance<ArchetypeTemplate>();
			template.name = name;
			template.AddToCache(name);
			temporaries.Add(template);
			return template;
		}

		private T NewBehaviour<T>(string hostName, ICharacter character) where T : CharacterPart
		{
			GameObject host = new GameObject(hostName);
			temporaries.Add(host);
			T behaviour = host.AddComponent<T>();
			behaviour.InitializeOnce(character);
			return behaviour;
		}

		[Test]
		public void EveryPlayerGetsAnArchetype_AndItIsSavedByTheModule()
		{
			FishMMO.UnitTests.Harness.LogAssert.IsTrue(CharacterParts.PartsFor(typeof(PlayerCharacter)).Contains(typeof(ArchetypeController)), "a player gets the archetype as a part");
			FishMMO.UnitTests.Harness.LogAssert.IsFalse(CharacterParts.PartsFor(typeof(NPC)).Contains(typeof(ArchetypeController)), "an NPC does not");
			FishMMO.UnitTests.Harness.LogAssert.IsTrue(CharacterPersistenceParticipants.All.Any(p => p is ArchetypePersistenceParticipant), "it loads and saves through the module");

			string guid = AssetDatabase.AssetPathToGUID(ProjectFiles.Path("Assets/Modules/Archetypes/Runtime/ArchetypeController.cs"));
			foreach (string race in new[] { "Human", "Elf", "Orc" })
			{
				FishMMO.UnitTests.Harness.LogAssert.IsFalse(ProjectFiles.ReadAllText($"Assets/Prefabs/Shared/Entity/PlayableCharacters/{race}.prefab").Contains(guid),
					$"{race}.prefab does not author the archetype controller");
			}
			string saving = ProjectFiles.ReadAllText("Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.Saving.cs");
			string loading = ProjectFiles.ReadAllText("Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.Loading.cs");
			FishMMO.UnitTests.Harness.LogAssert.IsFalse(saving.Contains("CharacterArchetypeData") || loading.Contains("CharacterArchetypeData"), "the core save and load name nothing of archetypes");
		}

		[Test]
		public void TheArchetypeSave_GoesThroughTheOwnershipGate_AndClearsItsMarkByTheOneRule()
		{
			string participant = ProjectFiles.ReadAllText("Assets/Modules/Archetypes/Server/ArchetypePersistence.cs");
			FishMMO.UnitTests.Harness.LogAssert.IsTrue(participant.Contains("await archetypeService.PersistOwnedAsync(archetypes, claims)") &&
				!participant.Contains("archetypeService.PersistAsync("), "the row is written under the character's claim, never ungated");
			FishMMO.UnitTests.Harness.LogAssert.IsTrue(participant.Contains("BulkWriteReporting.MayClearDirtyMarks(result)"),
				"its dirty mark clears only by the shared rule");
		}

		[Test]
		public void Archetype_RestorePath_InstallsWithoutRaisingAnything()
		{
			ArchetypeTemplate template = NewArchetype("ObserverShaping_Archetype");
			RecordingCharacter character = new RecordingCharacter(6);
			ArchetypeController controller = NewBehaviour<ArchetypeController>("ArchetypeRestore", character);

			bool changed = false;
			controller.OnArchetypeChanged += (a, b) => changed = true;

			// The spawn-payload and observer-broadcast path.
			controller.RestoreArchetype(template.ID);

			Assert.AreSame(template, controller.Template, "The restore path still installs the value.");
			Assert.IsFalse(changed, "A stranger streaming into range must not raise this character's archetype event on the onlooker.");
			Assert.AreEqual(0, character.InvokeCount, "Nor run that character's ECA triggers on the onlooker's machine.");
		}

		[Test]
		public void Archetype_AnnouncingPath_DispatchesEcaOnlyOnTheAuthoritativePeer()
		{
			ArchetypeTemplate template = NewArchetype("ObserverShaping_ArchetypeAnnounce");
			RecordingCharacter character = new RecordingCharacter(7);
			ArchetypeController controller = NewBehaviour<ArchetypeController>("ArchetypeAnnounce", character);

			bool changed = false;
			controller.OnArchetypeChanged += (a, b) => changed = true;

			controller.SetArchetype(template);

			Assert.AreSame(template, controller.Template);
			Assert.IsTrue(changed, "The UI-facing event stays on every peer that changed its own archetype.");
			Assert.AreEqual(0, character.InvokeCount,
				"This controller is not server-initialized, so the ECA dispatch must be gated off — actions are free to damage, grant and move.");
		}

		[Test]
		public void ArchetypeController_RestoreIsClean_ResetForgets()
		{
			GameObject host = new GameObject("ArchetypeHost");
			temporaries.Add(host);
			ArchetypeController controller = host.AddComponent<ArchetypeController>();
			controller.InitializeOnce(new RecordingCharacter(3));

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

	}
}
