using System.Linq;
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
	public class FactionPersistenceAuditTests
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

		private FactionTemplate NewFactionTemplate(string name)
		{
			FactionTemplate template = ScriptableObject.CreateInstance<FactionTemplate>();
			template.name = name;
			template.AddToCache(name);
			temporaries.Add(template);
			return template;
		}

		// ── Faction ──────────────────────────────────────────────────────────────

		[Test]
		public void Faction_MarkChanged_AdvancesVersion_AndOnlyAMatchingConfirmationClears()
		{
			FactionTemplate template = NewFactionTemplate("PersistenceAudit_Faction");
			Faction faction = new Faction(template.ID, 10);
			Assert.IsFalse(faction.PersistenceDirty, "A freshly constructed standing is what the constructor was given, not a change.");
			Assert.AreEqual(0, faction.Version);

			faction.MarkChanged();
			Assert.IsTrue(faction.PersistenceDirty);
			Assert.AreEqual(1, faction.Version);

			long snapshot = ++faction.Version;   // the save snapshot's own bump
			faction.MarkChanged();               // changed while the write was in flight
			faction.MarkPersisted(snapshot);
			Assert.IsTrue(faction.PersistenceDirty, "A confirmation for an older snapshot must not clear a newer change.");

			faction.MarkPersisted(faction.Version);
			Assert.IsFalse(faction.PersistenceDirty);
		}

		[Test]
		public void FactionController_GameplayMutationsDirty_RestoreDoesNot()
		{
			FactionTemplate template = NewFactionTemplate("PersistenceAudit_ControllerFaction");
			GameObject host = new GameObject("FactionHost");
			temporaries.Add(host);
			FactionController controller = host.AddComponent<FactionController>();
			controller.InitializeOnce(new MockCharacter(1));

			// The load path: install, stamp, confirm.
			controller.SetFaction(template.ID, 25, skipEvent: true);
			Assert.IsTrue(controller.Factions.TryGetValue(template.ID, out Faction restored));
			Assert.IsFalse(restored.PersistenceDirty, "skipEvent is the restore path and must not dirty.");
			restored.Version = 7;
			restored.MarkPersisted(7);
			Assert.IsFalse(restored.PersistenceDirty);

			controller.Add(template, 5);
			Assert.AreEqual(30, restored.Value);
			Assert.IsTrue(restored.PersistenceDirty, "A kill credit or quest reward is a change the database has not seen.");
			Assert.AreEqual(8, restored.Version, "Add advances the version from the loaded row's.");

			restored.MarkPersisted(8);
			controller.SetFaction(template.ID, 40);
			Assert.IsTrue(restored.PersistenceDirty, "SetFaction without skipEvent is a gameplay change.");
			Assert.AreEqual(9, restored.Version);
		}

		[Test]
		public void FactionController_ReadPayload_RefusesAFrameTooShortForItsHeader()
		{
			GameObject host = new GameObject("FactionReader");
			temporaries.Add(host);
			FactionController controller = host.AddComponent<FactionController>();
			controller.InitializeOnce(new MockCharacter(2));

			Writer writer = new Writer();
			writer.WriteUInt32Unpacked(0);          // a zero-length faction block
			writer.WriteInt32(123456);              // the NEXT behaviour's bytes
			Reader reader = new Reader(writer.GetArraySegment(), null);

			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			try
			{
				controller.ReadPayload(null, reader);
			}
			finally
			{
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
			}

			Assert.AreEqual(4, reader.Position, "The reader must stop at the block end, leaving the next behaviour's bytes untouched.");
			Assert.AreEqual(0, controller.Factions.Count);
		}

		// ── Attribute persistence state across pooling ───────────────────────────

		[Test]
		public void EveryCharacterGetsTheFactionPart_WhichTheSaveLegsAddress()
		{
			/* The faction controller is the module's part, added at wake to every character (players, NPCs, pets), not a
			 * component a prefab must carry; the save participant addresses it through IFactionStandings. */
			Assert.IsTrue(CharacterParts.PartsFor(typeof(PlayerCharacter)).Contains(typeof(FactionController)), "players get it");
			Assert.IsTrue(CharacterParts.PartsFor(typeof(NPC)).Contains(typeof(FactionController)), "NPCs get it");
			Assert.IsTrue(CharacterParts.PartsFor(typeof(Pet)).Contains(typeof(FactionController)), "pets get it");
		}

		[Test]
		public void TheFactionSave_WritesThroughTheGate_AndClearsItsMarksThroughTheOneRule()
		{
			string source = ProjectFiles.ReadAllText("Assets/Modules/Factions/Server/FactionPersistence.cs");
			Assert.IsTrue(source.Contains("await factionService.PersistOwnedAsync(factions, claims)"), "factions write through the ownership gate");
			Assert.IsFalse(source.Contains("await factionService.PersistAsync("), "and never through the ungated write");
			Assert.IsTrue(source.Contains("BulkWriteReporting.MayClearDirtyMarks(result)"), "a mark clears only through the one rule");
		}
	}
}
