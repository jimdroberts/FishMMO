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
	/// Pins the observer-payload shaping audit: what a non-owner is told about somebody else's
	/// character, and what has been taken off the wire entirely because nothing on the receiving
	/// side ever read it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Six of the eight fixes are here. The two that are not are the ones with no seam in this
	/// assembly: <c>FactionController</c>'s spawn-payload seeding of the owner and observer
	/// baselines, and its demand-driven tick subscription, both live behind <c>#if UNITY_SERVER</c>
	/// and the unit-test assembly is built <c>!UNITY_SERVER</c>. The rule those two paths consume —
	/// <see cref="FactionController.ObserverNeedsSignUpdate"/> — is pinned below, and the arm itself
	/// is type-checked by building <c>FishMMO.Shared</c> with <c>UNITY_SERVER</c> added to
	/// <c>DefineConstants</c>.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ObserverPayloadShapingTests
	{
		private readonly List<Object> temporaries = new List<Object>();

		/// <summary>How many trigger dispatches each character initiated during the current test.</summary>
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

		// ── Fixtures ─────────────────────────────────────────────────────────────

		/// <summary>Records ECA dispatches so a test can assert one did NOT happen.</summary>
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

		private T NewBehaviour<T>(string hostName, ICharacter character) where T : CharacterBehaviour
		{
			GameObject host = new GameObject(hostName);
			temporaries.Add(host);
			T behaviour = host.AddComponent<T>();
			behaviour.InitializeOnce(character);
			return behaviour;
		}

		/// <summary>Declared-only lookup, so an inherited implementation does not count as one.</summary>
		private static MethodInfo DeclaredMethod(System.Type type, string name)
		{
			return type.GetMethod(name,
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
		}

		// ── Defect 3: observers act on a sign, so a sign is what they are sent ───

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

		// ── Defect 1 / 6: state that is not on the wire at all ───────────────────

		[Test]
		public void WorldContainerContents_AreNotInTheSpawnPayload()
		{
			Assert.IsNull(DeclaredMethod(typeof(Container), "WritePayload"),
				"A chest's contents are viewer-scoped through ContainerOpenBroadcast; nothing on a client reads a world Container's Items.");
			Assert.IsNull(DeclaredMethod(typeof(Container), "ReadPayload"),
				"Writes and reads are paired: neither half may come back on its own.");

			// The scene-object ID still travels, which is what a client names to interact at all.
			Assert.IsNotNull(DeclaredMethod(typeof(Interactable), "WritePayload"));
			Assert.IsNotNull(DeclaredMethod(typeof(Interactable), "ReadPayload"));
		}

		[Test]
		public void GatheringNodeCharges_AreNotInTheSpawnPayload()
		{
			Assert.IsNull(DeclaredMethod(typeof(GatheringNode), "WritePayload"),
				"RemainingUses answered a question the node's existence already answers, and went stale the moment anyone gathered.");
			Assert.IsNull(DeclaredMethod(typeof(GatheringNode), "ReadPayload"));
		}

		[Test]
		public void GatheringNode_AllowsGathering_AsksTheChargesOnlyWhereTheyAreTrue()
		{
			// Not authoritative: defer, whatever the local count happens to say.
			Assert.IsTrue(GatheringNode.AllowsGathering(0, false));
			Assert.IsTrue(GatheringNode.AllowsGathering(-5, false));
			Assert.IsTrue(GatheringNode.AllowsGathering(3, false));

			// Authoritative: the count decides.
			Assert.IsTrue(GatheringNode.AllowsGathering(1, true));
			Assert.IsFalse(GatheringNode.AllowsGathering(0, true));
			Assert.IsFalse(GatheringNode.AllowsGathering(-1, true));
		}

		// ── Defect 5: a pet's orders are the owner's business ────────────────────

		[Test]
		public void PetSpawnPayload_IsIdenticalWhateverTheOrdersAre()
		{
			GameObject host = new GameObject("PetPayload");
			temporaries.Add(host);

			/* Adding the component runs NetworkBehaviour.Reset, which logs an error about duplicate
			 * NetworkObjects because Pet's RequireComponent chain brings one in more than once on a
			 * bare GameObject. That is an artefact of building the component by hand, not of the
			 * payload under test. */
			Pet pet;
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			try
			{
				pet = host.AddComponent<Pet>();
			}
			finally
			{
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
			}

			pet.Stance = PetStance.Defensive;
			pet.MovementOrder = PetMovementOrder.Follow;
			Writer defaults = new Writer();
			pet.WritePayload(null, defaults);

			pet.Stance = PetStance.Aggressive;
			pet.MovementOrder = PetMovementOrder.Stay;
			Writer commanded = new Writer();
			pet.WritePayload(null, commanded);

			CollectionAssert.AreEqual(defaults.GetArraySegment(), commanded.GetArraySegment(),
				"Stance and MovementOrder reach the owner in PetAddBroadcast. Nothing a non-owner reads depends on them, " +
				"so moving them must not move a single byte of the payload every observer receives.");
		}

		// ── Defect 8: one serialisation per message, not one per recipient ───────

		/// <summary>
		/// The same principle one step further out: a readout that needs a character's NAME is
		/// still better served by an ID, because the client already owns a disk-backed resolver for
		/// exactly that and every other panel goes through it.
		/// </summary>
		/// <remarks>
		/// This is not an observer payload — it answers a request — but it is the same defect
		/// shape, so it is pinned beside its relatives rather than in a fixture of its own.
		/// </remarks>
		[Test]
		public void InstanceDetails_NamesItsLeaderByID()
		{
			FieldInfo id = typeof(InstanceDetailsBroadcast).GetField("LeaderCharacterID");
			Assert.IsNotNull(id, "The leader must travel as an ID.");
			Assert.AreEqual(typeof(long), id.FieldType, "Character IDs are long everywhere else.");

			Assert.IsNull(typeof(InstanceDetailsBroadcast).GetField("LeaderName"),
				"The name must not travel alongside the ID — that is the redundancy, and it also " +
				"went out empty in precisely the case the ID is absent, so it never added reach.");

			/* The roster already did this correctly for members, which is where the shape came
			 * from: an ID to act on plus a name only because the server had already resolved it. */
			Assert.IsNotNull(typeof(InstanceMemberData).GetField("CharacterID"),
				"A kick may only ever name an ID.");
		}

		/// <summary>
		/// A name that arrives asynchronously must not be written into a model that has moved on.
		/// </summary>
		/// <remarks>
		/// The panel refreshes on a timer and clears its model on every answer, so a naming round
		/// trip can outlive the readout that asked for it. Without the identity check a slow answer
		/// would relabel a run under a previous leader's name, or repopulate a model cleared when
		/// the player left the instance. Pinned at source because the seam needs a live naming
		/// system and a mounted panel to exercise, and the guard is one line somebody could drop.
		/// </remarks>
		[Test]
		public void InstanceLeaderName_IsDiscardedWhenItArrivesLate()
		{
			string panel = ReadInstancePanelSource();

			int resolver = panel.IndexOf("private void ResolveLeaderName(long characterID)",
				System.StringComparison.Ordinal);
			Assert.GreaterOrEqual(resolver, 0, "The panel must resolve the leader's name from an ID.");

			int guard = panel.IndexOf("if (this.leaderCharacterID != requested)", resolver,
				System.StringComparison.Ordinal);
			Assert.GreaterOrEqual(guard, 0,
				"The callback must compare the ID it asked about against the current one before writing.");

			int write = panel.IndexOf("this.leaderName = name;", resolver, System.StringComparison.Ordinal);
			Assert.GreaterOrEqual(write, 0, "And then write the name.");
			Assert.Less(guard, write, "The guard must come before the write, or it guards nothing.");

			/* Zero is "there is a leader nobody here can identify", which is a different readout
			 * from "the name is still in flight". Collapsing them tells a player whose leader is
			 * standing next to them that the leader is not here. */
			Assert.IsTrue(panel.Contains("this.leaderCharacterID == 0"),
				"The readout must distinguish an unidentifiable leader from an unresolved name.");
		}

		// ── A character's race name is derived from its ID, never stored ─────────

		/// <summary>
		/// The race name must not exist as state anywhere on the character.
		/// </summary>
		/// <remarks>
		/// It was a string field assigned in <c>ReadPayload</c> that nothing in the tree ever
		/// assigned on the way out, so every reader got null. Removing it is only half the fix: the
		/// reason it must stay removed is that a name travelling separately from the ID it describes
		/// is a name that can disagree with it, and <see cref="IPlayerCharacter.RaceID"/> is the
		/// identity — it is what persists and what the template cache is keyed by.
		/// </remarks>
		[Test]
		public void CharacterRaceName_IsDerivedFromTheRaceID()
		{
			/* The race name may EXIST — it is part of what a character is — but it must not be
			 * STORED. A stored copy can disagree with the ID beside it, which is how the old field
			 * shipped null to every reader. Computed means there is nothing to disagree with.
			 *
			 * Checked by the absence of a backing field rather than by the absence of the member:
			 * an auto-property would compile to <RaceName>k__BackingField, so its absence is proof
			 * the accessor computes its answer. */
			Assert.IsNull(typeof(PlayerCharacter).GetField("RaceName",
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
				"A race name must not be held as a field.");
			Assert.IsNull(typeof(PlayerCharacter).GetField("<RaceName>k__BackingField",
					BindingFlags.Instance | BindingFlags.NonPublic),
				"Nor as an auto-property, which is a field with a nicer spelling.");
			Assert.IsNull(typeof(PlayerCharacter).GetField("<RaceTemplate>k__BackingField",
					BindingFlags.Instance | BindingFlags.NonPublic),
				"The template resolves from the ID too; a cached one would need invalidating.");

			PropertyInfo raceID = typeof(IPlayerCharacter).GetProperty("RaceID");
			Assert.IsNotNull(raceID, "The ID is the identity and must stay.");
			Assert.AreEqual(typeof(int), raceID.PropertyType,
				"It is RaceTemplate.ID, which is how the template cache is keyed.");

			/* On the interface, not an extension: a character's race is part of what a character
			 * IS, and every implementation owes the same answer. Derived in one place, so the two
			 * dictionary lookups are not copied out to every caller. The guild list and the
			 * character load still resolve from a bare int — a broadcast model and a database row,
			 * neither of which is a character — so they legitimately do not go through this. */
			PropertyInfo template = typeof(IPlayerCharacter).GetProperty("RaceTemplate");
			Assert.IsNotNull(template, "The template must be reachable from the character itself.");
			Assert.AreEqual(typeof(RaceTemplate), template.PropertyType);
			Assert.IsNull(template.SetMethod, "Derived state has no setter; RaceID is what moves.");

			PropertyInfo name = typeof(IPlayerCharacter).GetProperty("RaceName");
			Assert.IsNotNull(name, "And the name derived from it.");
			Assert.AreEqual(typeof(string), name.PropertyType);
			Assert.IsNull(name.SetMethod, "A settable race name is the stored field all over again.");

			/* The scene accessor moved the same way and for the same reason. It stays a method
			 * because IsInInstance beside it is one, and because every caller already spells it
			 * with parentheses. */
			Assert.IsNotNull(typeof(IPlayerCharacter).GetMethod("CurrentSceneName"),
				"The instance-aware scene name belongs to the character, not to a helper.");
		}

		private static string ReadInstancePanelSource()
		{
			string path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(),
				"Assets/Scripts/Client/GUI/World/Instance/UITKInstance.cs");
			Assert.IsTrue(ProjectFiles.Exists(path), $"UITKInstance.cs not found at {path}.");
			return ProjectFiles.ReadAllText(path);
		}
	}
}
