using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Serializing;
using KinematicCharacterController;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Regression guards for the defects found by the prediction-pipeline audit. Each
	/// test names the defect it pins and failed against the code as it was before the fix.
	/// </summary>
	[TestFixture]
	public class FactionPredictionAuditTests
	{
		private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

		[OneTimeSetUp]
		public void RegisterProductionSerializers()
		{
			Type[] serializerTypes =
			{
				typeof(CharacterReconcileDataDeltaSerializer),
				typeof(CharacterReplicateDataDeltaSerializer),
				typeof(CharacterTransientGroundingReportDeltaSerializer),
				typeof(KinematicCharacterMotorStateDeltaSerializer),
				typeof(CharacterAttributeResourceStateSerializer),
			};
			foreach (Type serializerType in serializerTypes)
			{
				MethodInfo register = serializerType.GetMethod("RegisterSerializers", Any);
				LogAssert.IsNotNull(register, $"{serializerType.Name} must expose a RegisterSerializers hook.");
				register.Invoke(null, null);
			}
		}


		private static CharacterReconcileData BaseReconcile()
		{
			CharacterReconcileData data = default;
			data.MotorState.Rotation = Quaternion.identity;
			data.MotorState.Position = new Vector3(10f, 2f, 10f);
			data.MotorState.BaseVelocity = new Vector3(1f, 0f, 0f);
			data.MotorState.TimeSinceJumpRequested = float.MaxValue;
			data.ResourceState.Health = 100;
			return data;
		}

		// ── 1. FishNet WriteDeltaVector3 leaked its Skip(1) placeholder into Writer.Length ──

		/// <summary>
		/// The owner's deferred resource values are applied after every behaviour has read.
		/// </summary>
		/// <remarks>
		/// A depletable value is clamped against its maximum, and the owner's maximum is not complete
		/// until the buff and equipment restores have run. <c>OnStartNetwork</c> is the first hook
		/// that runs after every behaviour on the object has read its payload — FishNet initialises
		/// objects in a later loop than the one that reads them — so it is where the clamp may
		/// finally run honestly.
		/// </remarks>
		[Test]
		public void DeferredResourceValues_AreAppliedAfterEveryBehaviourHasRead()
		{
			string path = Path.Combine(Application.dataPath,
				"FishMMO/Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterAttributeController.cs");
			string source = ProjectFiles.ReadAllText(path);
			int start = source.IndexOf("public override void OnStartNetwork()", StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, "CharacterAttributeController must override OnStartNetwork.");
			int call = source.IndexOf("ApplyDeferredResourceValues();", start, StringComparison.Ordinal);
			LogAssert.IsTrue(call > start && call - start < 800,
				"OnStartNetwork must apply the owner's deferred resource current values; it is the " +
				"first hook that runs after every behaviour on the object has read its payload, and " +
				"so the first point at which the maximum they are clamped against is complete.");
		}

		// ── 10. NPC factions: derived from the race template, not sent; pet rosters were discarded ──

		private static RaceTemplate MakeRace(string name, List<UnityEngine.Object> assets, out FactionTemplate alliedFaction, out FactionTemplate hostileFaction)
		{
			alliedFaction = ScriptableObject.CreateInstance<FactionTemplate>();
			alliedFaction.name = name + "_Allied"; alliedFaction.AddToCache(alliedFaction.name); assets.Add(alliedFaction);

			hostileFaction = ScriptableObject.CreateInstance<FactionTemplate>();
			hostileFaction.name = name + "_Hostile"; hostileFaction.AddToCache(hostileFaction.name); assets.Add(hostileFaction);

			FactionTemplate initial = ScriptableObject.CreateInstance<FactionTemplate>();
			initial.name = name + "_Initial";
			initial.DefaultAllied = new FactionTemplate.FactionHashSet { alliedFaction };
			initial.DefaultNeutral = new FactionTemplate.FactionHashSet();
			initial.DefaultHostile = new FactionTemplate.FactionHashSet { hostileFaction };
			initial.AddToCache(initial.name); assets.Add(initial);

			RaceTemplate race = ScriptableObject.CreateInstance<RaceTemplate>();
			race.name = name + "_Race";
			race.AddToCache(race.name); assets.Add(race);
			// The race's starting faction is the faction's to name (it was RaceTemplate.InitialFaction).
			initial.StartingRaces = new List<RaceTemplate> { race };
			FactionTemplate.InvalidateStartingRaces();
			return race;
		}

		/// <summary>
		/// An NPC's standings are a pure function of immutable template data, so the payload carries
		/// the race — which a spawner may have overridden server-side — and the receiver rebuilds the
		/// roster itself instead of being told it.
		/// </summary>
		[Test]
		public void NpcFactions_AreRebuiltFromTheRace_AndTheRaceOverrideTravels()
		{
			GameObject writerGo = new GameObject("FactionWriter");
			GameObject readerGo = new GameObject("FactionReader");
			List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
			try
			{
				RaceTemplate race = MakeRace("Audit_Npc", assets, out FactionTemplate allied, out FactionTemplate hostile);

				// Real NPCs: every character gets the faction part at wake, and the race is the NPC's own.
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
				NPC writerNpc = writerGo.AddComponent<NPC>();
				NPC readerNpc = readerGo.AddComponent<NPC>();
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
				// What BaseCharacter.Awake does in play (an edit-mode AddComponent runs no Awake).
				AttachParts(writerNpc);
				AttachParts(readerNpc);

				FactionController source = writerGo.GetComponent<FactionController>();
				LogAssert.IsTrue(source != null, "Every character carries the faction part.");
				writerNpc.SetRaceTemplateOnSpawn(race);   // what NPCSpawnableSettings.FactionOverride does

				LogAssert.IsTrue(source.FactionsAreTemplateDerived, "An NPC roster is derived.");
				LogAssert.IsTrue(source.Hostile.ContainsKey(hostile.ID), "Hostile defaults are rebuilt when the race changes.");
				LogAssert.IsTrue(source.Allied.ContainsKey(allied.ID), "Allied defaults are rebuilt when the race changes.");

				Writer payload = new Writer();
				source.WritePayload(null, payload);

				// The reader's NPC has no race yet: an empty roster.
				FactionController target = readerGo.GetComponent<FactionController>();
				LogAssert.AreEqual(0, target.Factions.Count, "The reader starts with nothing.");

				/* The NPC's own payload block carries the race and is read before the parts' blocks, so by the time the
				 * faction block is read the race is in place. */
				readerNpc.SetRaceTemplateOnSpawn(race);
				Reader reader = new Reader(payload.GetArraySegment(), null);
				target.ReadPayload(null, reader);
				LogAssert.AreEqual(0, reader.Remaining, "The faction block must be consumed exactly.");

				LogAssert.IsTrue(target.FactionsAreTemplateDerived, "The receiver rebuilds rather than being told.");
				LogAssert.AreEqual(race.ID, target.RaceTemplate.ID, "The faction part judges by the NPC's race.");
				LogAssert.AreEqual(source.Factions.Count, target.Factions.Count, "Both peers derive the same roster.");
				LogAssert.IsTrue(target.Hostile.ContainsKey(hostile.ID), "...including which factions are hostile.");

				/* And the roster itself is not on the wire: an owned roster of the same two entries
				 * costs more than a derived one, which carries only the race and a shape flag. */
				GameObject ownedGo = new GameObject("FactionOwned");
				FactionController owned = ownedGo.AddComponent<FactionController>();
				owned.InitializeOnce(new MockCharacter());
				owned.SetFaction(allied.ID, FactionTemplate.Maximum);
				owned.SetFaction(hostile.ID, FactionTemplate.Minimum);
				Writer ownedPayload = new Writer();
				owned.WritePayload(null, ownedPayload);
				LogAssert.IsTrue(ownedPayload.Position > payload.Position,
					$"A derived roster must not put its entries on the wire (derived {payload.Position} B, owned {ownedPayload.Position} B).");
				UnityEngine.Object.DestroyImmediate(ownedGo);
			}
			finally
			{
				foreach (UnityEngine.Object asset in assets) UnityEngine.Object.DestroyImmediate(asset);
				UnityEngine.Object.DestroyImmediate(writerGo);
				UnityEngine.Object.DestroyImmediate(readerGo);
			}
		}

		/// <summary>
		/// A pet's roster is copied from its owner and cannot be derived from any template, so it
		/// travels — and must survive arrival. <c>ReadPayload</c> used to install it through
		/// <c>SetFaction</c>, which refuses to move an NPC's standing, so every entry was dropped on
		/// the floor and a summoned pet reached its owner's client with an empty roster no matter
		/// what the server sent. A pet is an NPC, so the guard always fired.
		/// </summary>
		[Test]
		public void PetFactionRoster_CopiedFromItsOwner_SurvivesArrivalOnAnNpc()
		{
			GameObject ownerGo = new GameObject("FactionOwner");
			GameObject serverPetGo = new GameObject("FactionPetServer");
			GameObject clientPetGo = new GameObject("FactionPetClient");
			List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
			try
			{
				RaceTemplate race = MakeRace("Audit_Pet", assets, out FactionTemplate allied, out FactionTemplate hostile);

				FactionController ownerFactions = ownerGo.AddComponent<FactionController>();
				ownerFactions.InitializeOnce(new MockCharacter());
				ownerFactions.SetFaction(allied.ID, FactionTemplate.Maximum);
				ownerFactions.SetFaction(hostile.ID, FactionTemplate.Minimum);

				// Both ends are real pets: a Pet is an NPC, which is why the old guard always fired.
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
				Pet serverPet = serverPetGo.AddComponent<Pet>();
				Pet clientPet = clientPetGo.AddComponent<Pet>();
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
				AttachParts(serverPet);
				AttachParts(clientPet);

				FactionController serverFactions = serverPetGo.GetComponent<FactionController>();
				// Not LogAssert.IsNotNull: that helper stringifies the object, and FishNet's
				// NetworkBehaviour.ToString() dereferences network state an unspawned behaviour
				// does not have.
				LogAssert.IsTrue(serverFactions != null, "A pet gets the faction part at wake.");
				serverPet.SetRaceTemplateOnSpawn(race);
				LogAssert.IsTrue(serverFactions.FactionsAreTemplateDerived,
					"An NPC seeds a derived roster from its race as soon as it is assembled.");

				serverFactions.CopyFrom(ownerFactions);          // what PetSystem does before the spawn
				LogAssert.IsFalse(serverFactions.FactionsAreTemplateDerived,
					"A copied roster is owned state and must travel.");

				Writer payload = new Writer();
				serverFactions.WritePayload(null, payload);

				FactionController clientFactions = clientPetGo.GetComponent<FactionController>();
				LogAssert.IsTrue(clientFactions.Character as NPC != null,
					"A pet is an NPC; SetFaction would refuse every entry.");

				Reader reader = new Reader(payload.GetArraySegment(), null);
				clientFactions.ReadPayload(null, reader);
				LogAssert.AreEqual(0, reader.Remaining, "The faction block must be consumed exactly.");

				LogAssert.AreEqual(2, clientFactions.Factions.Count,
					"A pet must arrive holding the standings its owner handed it.");
				LogAssert.IsTrue(clientFactions.Hostile.ContainsKey(hostile.ID),
					"...grouped as its owner had them.");

				/* A non-owner receives the SIGN of each standing, not the standing itself — the
				 * payload is shaped per receiver, and the only thing any observer does with a peer's
				 * factions is ask GetAllianceLevel which side they are on. A pet is spawned with no
				 * owning connection, so every recipient reads the observer shape, including the
				 * summoner. What must survive arrival is the alliance stance, and it does: the two
				 * entries above are present and grouped exactly as the owner had them. */
				LogAssert.AreEqual(-1, clientFactions.Factions[hostile.ID].Value,
					"A hostile standing arrives as its sign, not as its magnitude.");

				// And the guard the restore path bypasses is still in force for gameplay adjustments.
				clientFactions.SetFaction(hostile.ID, FactionTemplate.Maximum);
				LogAssert.AreEqual(-1, clientFactions.Factions[hostile.ID].Value,
					"An NPC still refuses a gameplay faction adjustment.");
			}
			finally
			{
				foreach (UnityEngine.Object asset in assets) UnityEngine.Object.DestroyImmediate(asset);
				UnityEngine.Object.DestroyImmediate(ownerGo);
				UnityEngine.Object.DestroyImmediate(serverPetGo);
				UnityEngine.Object.DestroyImmediate(clientPetGo);
			}
		}


		private sealed class MockCharacter : ICharacter
		{
			public long ID { get; set; } = 77;
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
			public void EnableFlags(CharacterFlags flags) => Flags |= (int)flags;
			public void DisableFlags(CharacterFlags flags) => Flags &= ~(int)flags;
			public bool IsFlagged(CharacterFlags flags) => (Flags & (int)flags) != 0;
			public void RegisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
			public void UnregisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour { control = null; return false; }
			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}

		/// <summary>Attaches and initialises a character's declared parts, as <c>BaseCharacter.Awake</c> does in play.</summary>
		private static void AttachParts(BaseCharacter character)
		{
			CharacterParts.Attach(character);
			foreach (CharacterPart part in character.GetComponents<CharacterPart>())
			{
				part.InitializeOnce(character);
			}
		}
	}
}
