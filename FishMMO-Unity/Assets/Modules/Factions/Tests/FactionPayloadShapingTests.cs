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
	public class FactionPayloadShapingTests
	{
		private readonly List<Object> temporaries = new List<Object>();

		/// <summary>How many trigger dispatches each character initiated during the current test.</summary>
		private static readonly Dictionary<ICharacter, int> TriggerDispatches = new Dictionary<ICharacter, int>();

		[SetUp]
		public void CountTriggerDispatches()
		{
			// The dispatch hook (Trigger.DispatchObserved) is internal to the core tests; the faction tests do not count.
			TriggerDispatches.Clear();
		}

		[TearDown]
		public void TearDown()
		{
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

		private FactionTemplate NewFaction(string name)
		{
			FactionTemplate template = ScriptableObject.CreateInstance<FactionTemplate>();
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

		/// <summary>A character part on its own host, initialised for <paramref name="character"/> (no network: Host is null).</summary>
		private T NewPart<T>(string hostName, ICharacter character) where T : CharacterPart
		{
			GameObject host = new GameObject(hostName);
			temporaries.Add(host);
			T part = host.AddComponent<T>();
			part.InitializeOnce(character);
			return part;
		}

		/// <summary>Declared-only lookup, so an inherited implementation does not count as one.</summary>
		private static MethodInfo DeclaredMethod(System.Type type, string name)
		{
			return type.GetMethod(name,
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
		}

		// ── Defect 3: observers act on a sign, so a sign is what they are sent ───

		[Test]
		public void StandingSign_IsThreeState()
		{
			Assert.AreEqual(1, FactionController.StandingSign(1));
			Assert.AreEqual(1, FactionController.StandingSign(FactionTemplate.Maximum));
			Assert.AreEqual(0, FactionController.StandingSign(0));
			Assert.AreEqual(-1, FactionController.StandingSign(-1));
			Assert.AreEqual(-1, FactionController.StandingSign(FactionTemplate.Minimum));
		}

		[Test]
		public void StandingSign_SurvivesTheWireAndAnUnknownByteReadsNeutral()
		{
			Assert.AreEqual(1, FactionController.DecodeStandingSign(FactionController.EncodeStandingSign(4321)));
			Assert.AreEqual(0, FactionController.DecodeStandingSign(FactionController.EncodeStandingSign(0)));
			Assert.AreEqual(-1, FactionController.DecodeStandingSign(FactionController.EncodeStandingSign(-4321)));

			/* An out-of-range byte must read NEUTRAL, never allied. GetAllianceLevel turns "allied
			 * with my enemy" into Enemy, so a corrupt byte defaulting to +1 would paint a stranger
			 * red — the ambush that is not happening. */
			Assert.AreEqual(0, FactionController.DecodeStandingSign(3));
			Assert.AreEqual(0, FactionController.DecodeStandingSign(255));
		}

		[Test]
		public void ObserverNeedsSignUpdate_OnlyFiresOnACrossing()
		{
			// Never sent: observers hold nothing, so they are told whatever it is.
			Assert.IsTrue(FactionController.ObserverNeedsSignUpdate(false, 0, 0));
			Assert.IsTrue(FactionController.ObserverNeedsSignUpdate(false, 0, 5000));

			// A reputation tick inside one band is not news to anybody but the owner.
			Assert.IsFalse(FactionController.ObserverNeedsSignUpdate(true, 1, 1));
			Assert.IsFalse(FactionController.ObserverNeedsSignUpdate(true, 1, 32000));
			Assert.IsFalse(FactionController.ObserverNeedsSignUpdate(true, -1, -32000));
			Assert.IsFalse(FactionController.ObserverNeedsSignUpdate(true, 0, 0));

			// Crossings, in both directions and through neutral.
			Assert.IsTrue(FactionController.ObserverNeedsSignUpdate(true, 1, 0));
			Assert.IsTrue(FactionController.ObserverNeedsSignUpdate(true, 1, -1));
			Assert.IsTrue(FactionController.ObserverNeedsSignUpdate(true, 0, 1));
			Assert.IsTrue(FactionController.ObserverNeedsSignUpdate(true, -1, 1));
		}

		// ── The alliance level a standing belongs to, named once ─────────────────

		/// <summary>
		/// <c>GetAllianceLevelForStanding</c> is the sign of the standing, spelled as a level.
		/// </summary>
		/// <remarks>
		/// The reputation panel groups its rows by this, and the controller's own Allied / Neutral
		/// and Hostile tables are built on the same boundary — so this is the one statement of the
		/// partition both sides now read. <c>StandingSign(value) + 1</c> is not it: sign +1 is
		/// Enemy, which is exactly the inversion that would file allies under the ENEMIES header.
		/// </remarks>
		[Test]
		public void GetAllianceLevelForStanding_IsTheSignOfTheStanding()
		{
			Assert.AreEqual(FactionAllianceLevel.Ally, FactionController.GetAllianceLevelForStanding(1));
			Assert.AreEqual(FactionAllianceLevel.Ally, FactionController.GetAllianceLevelForStanding(FactionTemplate.Maximum));
			Assert.AreEqual(FactionAllianceLevel.Neutral, FactionController.GetAllianceLevelForStanding(0));
			Assert.AreEqual(FactionAllianceLevel.Enemy, FactionController.GetAllianceLevelForStanding(-1));
			Assert.AreEqual(FactionAllianceLevel.Enemy, FactionController.GetAllianceLevelForStanding(FactionTemplate.Minimum));

			/* Every standing the templates allow, so no boundary is a special case. 20001 iterations
			 * of a three-way branch, which is cheaper than being wrong about one of them. */
			for (int value = FactionTemplate.Minimum; value <= FactionTemplate.Maximum; ++value)
			{
				FactionAllianceLevel expected =
					value > 0 ? FactionAllianceLevel.Ally :
					value < 0 ? FactionAllianceLevel.Enemy :
					FactionAllianceLevel.Neutral;

				Assert.AreEqual(expected, FactionController.GetAllianceLevelForStanding(value),
					$"a standing of {value} belongs to {expected}");
			}

			/* The panel uses the enum's own numeric value as its section index, so the enum's
			 * declaration order IS the display order the issue asks for: allies first, neutrals in
			 * the middle, enemies last. Reordering the members would silently reorder the panel. */
			Assert.Less((byte)FactionAllianceLevel.Ally, (byte)FactionAllianceLevel.Neutral,
				"allies must sort before neutrals");
			Assert.Less((byte)FactionAllianceLevel.Neutral, (byte)FactionAllianceLevel.Enemy,
				"and neutrals before enemies");
		}

		/// <summary>
		/// The level the helper names is the table the controller actually files the standing in.
		/// </summary>
		/// <remarks>
		/// Two definitions of one partition is one more than can be kept in step by reading them.
		/// <c>ApplyFactionValue</c> files through the inlined <c>InsertToAllianceGroup</c>; this
		/// drives a real controller and asserts that each standing lands in exactly the dictionary
		/// the helper names, converting the agreement into something checked.
		/// </remarks>
		[Test]
		public void GetAllianceLevelForStanding_NamesTheTableTheControllerFilesInto()
		{
			FactionTemplate allied = NewFaction("ObserverShaping_AgreementAlly");
			FactionTemplate neutral = NewFaction("ObserverShaping_AgreementNeutral");
			FactionTemplate hostile = NewFaction("ObserverShaping_AgreementEnemy");

			FactionController controller = NewPart<FactionController>("FactionAgreement", new RecordingCharacter(8));
			controller.SetFaction(allied.ID, 4321, skipEvent: true);
			controller.SetFaction(neutral.ID, 0, skipEvent: true);
			controller.SetFaction(hostile.ID, -8765, skipEvent: true);

			Assert.AreEqual(3, controller.Factions.Count, "all three standings must be installed");

			foreach (Faction faction in controller.Factions.Values)
			{
				FactionAllianceLevel level = FactionController.GetAllianceLevelForStanding(faction.Value);

				Dictionary<int, Faction> named;
				switch (level)
				{
					case FactionAllianceLevel.Ally: named = controller.Allied; break;
					case FactionAllianceLevel.Enemy: named = controller.Hostile; break;
					default: named = controller.Neutral; break;
				}

				Assert.IsTrue(named.ContainsKey(faction.Template.ID),
					$"{faction.Template.Name} has a standing of {faction.Value}, so the helper calls it " +
					$"{level} — and the controller must have filed it in that table");

				// And in exactly one, since a row cannot be in two sections at once.
				int tables = (controller.Allied.ContainsKey(faction.Template.ID) ? 1 : 0)
					+ (controller.Neutral.ContainsKey(faction.Template.ID) ? 1 : 0)
					+ (controller.Hostile.ContainsKey(faction.Template.ID) ? 1 : 0);
				Assert.AreEqual(1, tables, "a faction belongs to one alliance level, not several");
			}

			// A crossing moves it between tables, and the helper moves with it.
			controller.SetFaction(allied.ID, -1, skipEvent: true);
			Assert.IsFalse(controller.Allied.ContainsKey(allied.ID), "a crossed row leaves the allied table");
			Assert.IsTrue(controller.Hostile.ContainsKey(allied.ID), "and arrives in the hostile one");
			Assert.AreEqual(FactionAllianceLevel.Enemy,
				FactionController.GetAllianceLevelForStanding(controller.Factions[allied.ID].Value),
				"which is the level the helper names for it now");
		}

		[Test]
		public void FactionPayload_NonOwnerReceivesSignsRatherThanStandings()
		{
			FactionTemplate allied = NewFaction("ObserverShaping_Allied");
			FactionTemplate neutral = NewFaction("ObserverShaping_Neutral");
			FactionTemplate hostile = NewFaction("ObserverShaping_Hostile");

			FactionController source = NewPart<FactionController>("FactionSource", new RecordingCharacter(1));
			source.SetFaction(allied.ID, 4321, skipEvent: true);
			source.SetFaction(neutral.ID, 0, skipEvent: true);
			source.SetFaction(hostile.ID, -8765, skipEvent: true);

			/* conn is null, so PayloadVisibility.IsOwner answers false — the filtered direction, and
			 * the shape every connection but the owner receives. */
			Writer writer = new Writer();
			source.WritePayload(null, writer);

			FactionController receiver = NewPart<FactionController>("FactionReceiver", new RecordingCharacter(2));
			receiver.ReadPayload(null, new Reader(writer.GetArraySegment(), null));

			Assert.AreEqual(3, receiver.Factions.Count);
			Assert.AreEqual(1, receiver.Factions[allied.ID].Value,
				"A non-owner holds a SIGN. 4321 must not survive the trip; a bystander is not entitled to a peer's exact standing.");
			Assert.AreEqual(0, receiver.Factions[neutral.ID].Value);
			Assert.AreEqual(-1, receiver.Factions[hostile.ID].Value);

			// The alliance tables — the only thing GetAllianceLevel consults — are unchanged by the reshaping.
			Assert.IsTrue(receiver.Allied.ContainsKey(allied.ID));
			Assert.IsTrue(receiver.Neutral.ContainsKey(neutral.ID));
			Assert.IsTrue(receiver.Hostile.ContainsKey(hostile.ID));
		}

		[Test]
		public void FactionPayload_OwnerShapeStillCarriesExactStandings()
		{
			FactionTemplate faction = NewFaction("ObserverShaping_OwnerExact");

			/* Hand-written because PayloadVisibility.IsOwner cannot answer true without a spawned
			 * NetworkObject. This is the owner's shape, byte for byte: race id, "not derived",
			 * shape 0, one entry. */
			Writer inner = new Writer();
			inner.WriteInt32Unpacked(0);      // race id: none
			inner.WriteBoolean(false);        // aggression off
			inner.WriteUInt32Unpacked(0u);    // no lock
			inner.WriteBoolean(false);        // an owned roster, not a derived one
			inner.WriteUInt8Unpacked(0);      // FACTION_PAYLOAD_SHAPE_VALUES
			inner.WriteInt32(1);              // one entry
			inner.WriteInt32Unpacked(faction.ID);
			inner.WriteInt32(4321);

			Writer framed = new Writer();
			framed.WriteUInt32Unpacked((uint)inner.Length);
			framed.WriteArraySegment(inner.GetArraySegment());

			FactionController receiver = NewPart<FactionController>("FactionOwnerReceiver", new RecordingCharacter(3));
			receiver.ReadPayload(null, new Reader(framed.GetArraySegment(), null));

			Assert.AreEqual(1, receiver.Factions.Count);
			Assert.AreEqual(4321, receiver.Factions[faction.ID].Value,
				"The owner's own faction panel renders the numbers, so the owner's shape keeps full precision.");
		}

		[Test]
		public void FactionPayload_DerivedRosterFrameIsTenBytesAndIsAccepted()
		{
			/* Every ordinary NPC in the world writes exactly this: an unpacked race id and the
			 * derived flag, and nothing after it. The reader's minimum-length guard has to admit a
			 * five-byte frame or every NPC's faction block is rejected and its race never arrives. */
			Writer inner = new Writer();
			inner.WriteInt32Unpacked(0);
			inner.WriteBoolean(false);        // aggression off
			inner.WriteUInt32Unpacked(0u);    // no lock
			inner.WriteBoolean(true);
			Assert.AreEqual(10, inner.Length, "A derived roster's frame is the race id, the aggression state and the flag.");

			Writer framed = new Writer();
			framed.WriteUInt32Unpacked((uint)inner.Length);
			framed.WriteArraySegment(inner.GetArraySegment());
			int frameEnd = framed.Length;
			framed.WriteInt32Unpacked(0x5EC0ADDE);   // the NEXT behaviour's bytes

			FactionController receiver = NewPart<FactionController>("FactionDerivedReceiver", new RecordingCharacter(4));
			Reader reader = new Reader(framed.GetArraySegment(), null);
			receiver.ReadPayload(null, reader);

			Assert.AreEqual(frameEnd, reader.Position, "The reader must land exactly on the frame end.");
			Assert.AreEqual(0x5EC0ADDE, reader.ReadInt32Unpacked(), "The behaviour after this one must still be aligned.");
		}

		[Test]
		public void FactionPayload_UnrecognisedShapeSeeksToTheFrameEndRatherThanGuessing()
		{
			Writer inner = new Writer();
			inner.WriteInt32Unpacked(0);
			inner.WriteBoolean(false);        // aggression off
			inner.WriteUInt32Unpacked(0u);    // no lock
			inner.WriteBoolean(false);
			inner.WriteUInt8Unpacked(200);    // a shape this build does not know
			inner.WriteInt32(1);
			inner.WriteInt32Unpacked(1234);
			inner.WriteInt32(4321);

			Writer framed = new Writer();
			framed.WriteUInt32Unpacked((uint)inner.Length);
			framed.WriteArraySegment(inner.GetArraySegment());
			int frameEnd = framed.Length;
			framed.WriteInt32Unpacked(0x0B00B1E5);

			FactionController receiver = NewPart<FactionController>("FactionBadShapeReceiver", new RecordingCharacter(5));
			Reader reader = new Reader(framed.GetArraySegment(), null);

			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
			try
			{
				receiver.ReadPayload(null, reader);
			}
			finally
			{
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
			}

			Assert.AreEqual(0, receiver.Factions.Count);
			Assert.AreEqual(frameEnd, reader.Position, "The entry width follows from the shape, so an unknown shape must seek, not walk.");
			Assert.AreEqual(0x0B00B1E5, reader.ReadInt32Unpacked());
		}

		// ── Defect 4: an observed archetype announces nothing ────────────────────

		[Test]
		public void ObserverFanOut_GoesThroughTheSharedScope()
		{
			Assert.IsNull(DeclaredMethod(typeof(FactionController), "BroadcastToObserversOnly"),
				"The hand-rolled loop serialised the struct once per connection; ObserverBroadcastScope writes it once.");
			Assert.IsNotNull(DeclaredMethod(typeof(ObserverBroadcastScope), "BroadcastToObserversExceptOwner"));
		}

		// ── The instance readout names its leader from an ID, not from a string ──

	}
}
