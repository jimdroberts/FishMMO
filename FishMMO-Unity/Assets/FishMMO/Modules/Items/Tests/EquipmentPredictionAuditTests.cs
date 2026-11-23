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
using FishNet.Serializing;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The predicted equipment's audit pins (the combat audit and its follow-ups, the observer synchronisation
	/// proofs, the prediction audit regressions), moved out of the core prediction fixtures with the controller (the Items
	/// module). The core fixtures keep the attribute, buff, cooldown and motor halves.
	/// </summary>
	[TestFixture]
	public class EquipmentPredictionAuditTests
	{
		private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

		private readonly List<GameObject> gameObjects = new List<GameObject>();
		private readonly List<UnityEngine.Object> assets = new List<UnityEngine.Object>();

		[TearDown]
		public void TearDown()
		{
			foreach (UnityEngine.Object asset in assets)
			{
				if (asset is ICachedObject cached) cached.RemoveFromCache();
				UnityEngine.Object.DestroyImmediate(asset);
			}
			assets.Clear();
			foreach (GameObject go in gameObjects)
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
			gameObjects.Clear();
		}

		#region The spawn payload (combat audit F2; prediction audit regressions)

		/// <summary>
		/// Reading an OBSERVER-shaped equipment payload must not run the item's attribute apply;
		/// reading an OWNER-shaped one must.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <c>ReadPayload</c> called <c>ItemEquippable.Equip</c> for BOTH shapes.
		/// <c>Equip</c> raises <c>OnEquip</c>, which runs <c>ItemGenerator.ApplyAttributes</c> — and
		/// the observer's attribute payload from the same spawn already carries the server's TOTAL
		/// <c>ExternalModifier</c>, which by construction contains every equipped item. That is the
		/// identical double-apply <c>ApplyObservedSlot</c> was rewritten to remove for the broadcast
		/// path, and the rule <c>SimulatesEquipmentEffects</c> states; only the payload path was
		/// never brought into line.
		/// </para>
		/// <para>
		/// <b>It was inert, not harmless.</b> The observer shape omits the item id, so
		/// <c>ItemGenerator.TryResolveLedgerSource</c> declined for a zero id and wrote no ledger
		/// entry. An accident of the wire format was standing in for a rule, and it would have become
		/// a live double-apply the moment the observer payload carried an id — so the difference
		/// cannot be measured on the attribute value, and is measured here on whether the apply path
		/// RAN at all.
		/// </para>
		/// <para>
		/// The probe is <c>ICharacter.TryGet</c>, which <c>ApplyAttributes</c> calls unconditionally
		/// before it looks at anything else. Both receivers read a payload written from the same item
		/// in the same slot, so the only difference between them is the shape byte and the one call
		/// the equip handler makes.
		/// </para>
		/// </remarks>
		[Test]
		public void ObserverEquipmentPayload_PointsTheItemAtTheCharacter_WithoutApplyingItsBonuses()
		{
			GameObject senderHost = new GameObject("F2_Sender");
			GameObject ownerHost = new GameObject("F2_OwnerReceiver");
			GameObject observerHost = new GameObject("F2_ObserverReceiver");
			FixArmorTemplate template = ScriptableObject.CreateInstance<FixArmorTemplate>();

			try
			{
				template.name = "F2_GeneratedArmor";
				template.Slot = ItemSlot.Chest;
				// Generated, so the item wires ItemEquippable_OnEquip -> Generator.ApplyAttributes.
				// That handler is the entire difference between the two paths.
				template.Generate = true;
				/* Explicitly empty rather than left null. Unity serialises an authored asset's list
				 * as empty, never null, so ItemGenerator.AddAdditionalTemplateAttributes iterates it
				 * unguarded — a CreateInstance template would otherwise NRE inside the constructor
				 * for a reason that has nothing to do with what is being tested. */
				template.Attributes = new System.Collections.Generic.List<ItemAttributeTemplate>();
				template.RandomAttributeDatabases = new ItemAttributeTemplateDatabase[0];
				template.AddToCache(template.name);

				EquipmentController sender = senderHost.AddComponent<EquipmentController>();
				// OnAwake is what allocates the slot array; AddComponent alone does not run it here.
				sender.OnAwake();
				sender.InitializeOnce(new CountingCharacter());

				// Id zero on purpose: the observer shape does not carry one, so writing zero keeps
				// the two payloads byte-identical apart from the shape flag.
				Item item = new Item(0L, seed: 12345, template, 1u);
				LogAssert.IsTrue(item.IsGenerated, "The probe needs a generated item — that is what wires OnEquip.");
				LogAssert.IsTrue(item.IsEquippable, "...and an equippable one.");
				LogAssert.IsTrue(sender.SetItemSlot(item, (int)ItemSlot.Chest),
					"The sender must actually hold the item, or both payloads are empty and the test proves nothing.");

				int ownerCalls = ReadShapeAndCountAttributeLookups(sender, ownerHost, ownerShape: true, out Item ownerItem);
				int observerCalls = ReadShapeAndCountAttributeLookups(sender, observerHost, ownerShape: false, out Item observerItem);

				LogAssert.IsNotNull(ownerItem, "The owner must end up holding the item.");
				LogAssert.IsNotNull(observerItem, "The observer must end up holding the item — it needs the mesh.");

				LogAssert.IsNotNull(observerItem.Equippable.Character,
					"The observer's item must still be POINTED at the character. ItemGenerator.SetAttribute " +
					"reads Equippable.Character to decide whether a later change has anywhere to go, so " +
					"suppressing the bonuses must not also drop the owner link.");

				LogAssert.AreEqual(ownerCalls - 1, observerCalls,
					$"The owner ran the attribute apply and the observer must not: owner made {ownerCalls} " +
					$"controller lookups, observer {observerCalls}. Equal counts mean the observer is " +
					"calling Equip for real and applying item bonuses on top of the server total it was " +
					"just sent in the same spawn.");
			}
			finally
			{
				template.RemoveFromCache();
				UnityEngine.Object.DestroyImmediate(template);
				UnityEngine.Object.DestroyImmediate(senderHost);
				UnityEngine.Object.DestroyImmediate(ownerHost);
				UnityEngine.Object.DestroyImmediate(observerHost);
			}
		}
		/// <summary>
		/// Writes <paramref name="sender"/>'s equipment in one shape, reads it into a fresh
		/// controller, and reports how many times the receiving character was asked for its attribute
		/// controller.
		/// </summary>
		private static int ReadShapeAndCountAttributeLookups(
			EquipmentController sender, GameObject host, bool ownerShape, out Item received)
		{
			Writer writer = new Writer();
			sender.WritePayload(writer, ownerShape);

			CountingCharacter character = new CountingCharacter();
			EquipmentController receiver = host.AddComponent<EquipmentController>();
			receiver.OnAwake();
			receiver.InitializeOnce(character);

			character.AttributeControllerLookups = 0;
			Reader reader = new Reader(writer.GetArraySegment(), null);
			receiver.ReadPayload(null, reader);

			LogAssert.AreEqual(0, reader.Remaining,
				"The framed block must be consumed exactly, or every behaviour after this one desyncs.");

			received = receiver.Items[(int)ItemSlot.Chest];
			return character.AttributeControllerLookups;
		}
		/// <summary>
		/// Item templates are immutable assets registered before anything spawns, so an id with no
		/// template means the payload is corrupt — but the reader used to hand it straight to
		/// <c>new Item(...)</c>, whose constructor dereferences <c>Template</c>. The throw escaped
		/// <c>ReadPayload</c>, past the frame that exists to keep the stream aligned, taking every
		/// later behaviour on the object and the rest of the spawn packet with it.
		/// </summary>
		[Test]
		public void EquipmentPayload_WithAnUnresolvableTemplate_DoesNotThrow_AndStaysFramed()
		{
			GameObject go = new GameObject("EquipmentPayloadProbe");
			try
			{
				EquipmentController equipment = go.AddComponent<EquipmentController>();
				equipment.InitializeOnce(new MockCharacter());

				// Hand-built observer-shape block carrying one item whose template cannot resolve,
				// then a sentinel that the next behaviour would read.
				Writer writer = new Writer();
				writer.Skip(4);
				int blockStart = writer.Position;
				writer.WriteUInt8Unpacked(0);          // observer shape
				writer.WriteInt32(1);                  // one item
				writer.WriteInt32(int.MaxValue);       // template id that resolves to null
				writer.WriteUInt8Unpacked(3);          // slot
				writer.WriteInt32(1234);               // seed
				writer.InsertUInt32Unpacked((uint)(writer.Position - blockStart), blockStart - 4);
				const int sentinel = 0x5A5A5A;
				writer.WriteInt32(sentinel);

				Reader reader = new Reader(writer.GetArraySegment(), null);
				UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
				try
				{
					Assert.DoesNotThrow(() => equipment.ReadPayload(null, reader),
						"An unresolvable item template must not throw out of a spawn payload read.");
				}
				finally
				{
					UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
				}

				LogAssert.AreEqual(sentinel, reader.ReadInt32(),
					"The behaviour after equipment must still read its own bytes.");
				LogAssert.AreEqual(0, reader.Remaining, "The block must be consumed exactly.");
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}
		#endregion

		#region The observer route (observer synchronisation proofs; prediction audit regressions)

		/// <summary>
		/// An observed-slot broadcast reaches an observer, fills the slot with the template and
		/// seed a mesh is chosen from, and a later empty message clears it.
		/// </summary>
		[Test]
		public void Equipment_SlotChangesReachTheObserver_AndClearAgain()
		{
			EquipmentController observer = MakeEquipmentController("ObserverEquipment");

			BaseItemTemplate template = MakeItemTemplate("ProofSword");

			/* Seed 0: the probe template is not marked Generate, so the Item it produces has no
			 * ItemGenerator and reports its seed as 0. ApplyObservedSlot's idempotence test reads
			 * `current.IsGenerated ? current.Generator.Seed : 0`, so a non-zero seed here would
			 * never match and every re-apply would rebuild — an artefact of the probe, not of the
			 * production path. That the seed FIELD survives the wire is asserted separately. */
			EquipmentObservedSlotBroadcast received = RoundTripEquipmentSlot(new EquipmentObservedSlotBroadcast
			{
				CharacterObjectID = 1234,
				Slot = (byte)ItemSlot.Primary,
				TemplateID = template.ID,
				Seed = 0,
			});

			// The seed is what picks a generated item's appearance; prove it crosses the wire.
			EquipmentObservedSlotBroadcast seeded = RoundTripEquipmentSlot(new EquipmentObservedSlotBroadcast
			{
				CharacterObjectID = 1234, Slot = (byte)ItemSlot.Primary, TemplateID = template.ID, Seed = 4242,
			});
			LogAssert.AreEqual(4242, seeded.Seed, "The generation seed must survive the wire.");
			LogAssert.AreEqual(template.ID, seeded.TemplateID, "The template id must survive the wire.");
			LogAssert.AreEqual((byte)ItemSlot.Primary, seeded.Slot, "The slot must survive the wire.");

			LogAssert.IsFalse(received.IsEmpty, "A message naming a template is not an empty-slot message.");
			ApplyObservedSlot(observer, received);

			LogAssert.IsTrue(observer.TryGetItem((int)ItemSlot.Primary, out Item equipped),
				"The observer's slot must be filled after the broadcast.");
			LogAssert.IsNotNull(equipped, "The slot must hold a real item.");
			LogAssert.AreEqual(template.ID, equipped.Template.ID,
				"The template is what EquipmentVisualController picks a mesh from; it must arrive.");

			// Applying the identical message again must be a no-op, which is what makes the spawn
			// payload and a broadcast that raced it safe to apply in either order.
			Item first = equipped;
			ApplyObservedSlot(observer, received);
			observer.TryGetItem((int)ItemSlot.Primary, out Item second);
			LogAssert.AreSame(first, second,
				"Re-applying the same template and seed must not rebuild the item, or the visual " +
				"controller would redraw on every duplicate.");

			// Emptying the slot.
			EquipmentObservedSlotBroadcast cleared = RoundTripEquipmentSlot(new EquipmentObservedSlotBroadcast
			{
				CharacterObjectID = 1234,
				Slot = (byte)ItemSlot.Primary,
				TemplateID = 0,
				Seed = 0,
			});
			LogAssert.IsTrue(cleared.IsEmpty, "A zero template id is the empty-slot signal.");
			ApplyObservedSlot(observer, cleared);

			observer.TryGetItem((int)ItemSlot.Primary, out Item afterClear);
			LogAssert.IsNull(afterClear, "An empty-slot message must clear the observer's slot.");
		}
		/// <summary>
		/// The same double-count on the observer route: watching a peer equip a generated item used
		/// to add that item's bonuses to the observer's copy of the peer's sheet, on top of the
		/// authoritative total the server already broadcasts.
		/// </summary>
		[Test]
		public void ObservedEquipSlot_DoesNotApplyAttributeModifiersLocally()
		{
			GameObject go = new GameObject("ObservedEquipProbe");
			List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
			try
			{
				MockCharacter character = new MockCharacter();
				CharacterAttributeController attributes = go.AddComponent<CharacterAttributeController>();
				attributes.InitializeOnce(character);

				CharacterAttributeTemplate attributeTemplate = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
				attributeTemplate.name = "Audit_Observed_Armor"; attributeTemplate.InitialValue = 10;
				attributeTemplate.AddToCache(attributeTemplate.name); assets.Add(attributeTemplate);
				CharacterAttribute armor = new CharacterAttribute(attributes, attributeTemplate.ID, 10, 0);
				attributes.AddAttribute(armor);

				// What the server's authoritative attribute broadcast installed, gear included.
				armor.SetModifier(40);

				EquipmentController equipment = go.AddComponent<EquipmentController>();
				equipment.InitializeOnce(character);

				MethodInfo applyObserved = typeof(EquipmentController).GetMethod("ApplyObservedSlot", Any);
				LogAssert.IsTrue(applyObserved != null, "EquipmentController must expose ApplyObservedSlot.");
				// Template id 0 clears the slot; an unknown id is refused before any equip. Either
				// way the observer path must leave the attribute sheet exactly as broadcast.
				applyObserved.Invoke(equipment, new object[] { 0, 0, 0 });

				LogAssert.AreEqual(40, armor.ExternalModifier,
					"An observed equipment change must not recompute another character's attributes locally.");

				string source = ProjectFiles.ReadAllText(Path.Combine(Application.dataPath,
					"FishMMO/Modules/Items/Runtime/EquipmentController.cs"));
				int start = source.IndexOf("internal void ApplyObservedSlot", StringComparison.Ordinal);
				int end = source.IndexOf("// ── Owner acknowledgements", start, StringComparison.Ordinal);
				LogAssert.IsTrue(start >= 0 && end > start, "ApplyObservedSlot must be locatable.");
				string body = source.Substring(start, end - start);
				LogAssert.IsTrue(body.Contains("SetEquippedCharacterSilently"),
					"The observer path must equip silently — Equip() runs ItemGenerator.ApplyAttributes.");
				LogAssert.IsFalse(body.Contains("Equippable.Equip(Character)"),
					"The observer path must not call the attribute-applying Equip.");
				LogAssert.IsFalse(body.Contains("DetachFromSlot(current, (byte)slot);"),
					"The observer path's detach must be silent too, or it subtracts what it never added.");
			}
			finally
			{
				foreach (UnityEngine.Object asset in assets) UnityEngine.Object.DestroyImmediate(asset);
				UnityEngine.Object.DestroyImmediate(go);
			}
		}
		/// <summary>
		/// <c>EquipmentController</c> no longer carries its own copy of the recipient collection.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The copy was line-for-line identical to <see cref="ObserverBroadcastScope"/>, which is the
		/// one place that documents why the copy is mandatory at all:
		/// <c>ServerManager.BroadcastExcept</c> calls <c>Remove</c> on the set it is handed, so
		/// passing <c>NetworkObject.Observers</c> to it does not exclude the owner for one message —
		/// it permanently drops the owner from that object's observer set.
		/// </para>
		/// <para>
		/// A second implementation of a rule that subtle is how the next one gets it wrong. The copy
		/// also never cleared its static set after a send, so it retained a connection reference per
		/// observer between equipment pushes.
		/// </para>
		/// </remarks>
		[Test]
		public void EquipmentController_DoesNotReimplementTheObserverBroadcastScope()
		{
			LogAssert.IsNull(
				typeof(EquipmentController).GetMethod("CollectObserverRecipients", Any),
				"Equipment must broadcast through ObserverBroadcastScope. A private copy of the " +
				"BroadcastExcept rule is how the owner gets dropped from an observer set.");

			LogAssert.IsNull(
				typeof(EquipmentController).GetField("observerRecipients", Any),
				"...and its scratch recipient set goes with it; the shared one clears itself after " +
				"each send, this one did not.");
		}
		/// <summary>
		/// The equipment's reconcile opens with the non-owner guard (ObserverSyncMode): with two writers a phantom item sits in
		/// one slot. The core's <c>ModeGuards_EveryReconcileConsumerConsultsObserverSyncMode</c> pins the core controllers.
		/// </summary>
		[Test]
		public void ModeGuard_TheEquipmentReconcileConsultsObserverSyncMode()
		{
			string source = ProjectFiles.ReadAllText(Path.Combine(Application.dataPath, "FishMMO/Modules/Items/Runtime/EquipmentController.cs"));
			int reconcile = source.IndexOf("public void OnReconcile(", StringComparison.Ordinal);
			LogAssert.IsTrue(reconcile >= 0, "EquipmentController must implement OnReconcile.");
			int open = source.IndexOf('{', reconcile);
			int depth = 0, end = open;
			for (int i = open; i < source.Length; i++)
			{
				if (source[i] == '{') depth++;
				else if (source[i] == '}' && --depth == 0) { end = i; break; }
			}
			LogAssert.IsTrue(source.Substring(open, end - open).Contains("ObserverSyncMode.ObserversConsumeReconcile"),
				"EquipmentController.OnReconcile must open with the non-owner guard — two writers put a phantom item in one slot. " +
				"See ObserverSyncMode: every reconcile consumer asks it whose turn it is.");
		}

		/// <summary>
		/// Equipment teardown does not reverse bonuses on a peer that never applied them.
		/// </summary>
		/// <remarks>
		/// <c>Item.Destroy</c> unequips before detaching its handlers — deliberately, so a real
		/// unequip cannot orphan its modifiers — and <c>SetEquippedCharacterSilently</c> re-attaches
		/// <c>OnUnequip</c> after suppressing only the equip half. So an observer's
		/// <c>ResetState</c> ran <c>ItemGenerator.RemoveAttributes</c> for gear whose bonuses the
		/// server had already accounted for in the broadcast <c>ExternalModifier</c>.
		/// </remarks>
		[Test]
		public void EquipmentResetState_DoesNotReverseModifiersOnATrackingOnlyPeer()
		{
			string source = ProjectFiles.ReadAllText(Path.Combine(Application.dataPath,
				"FishMMO/Modules/Items/Runtime/EquipmentController.cs"));

			LogAssert.IsTrue(source.Contains("SimulatesEquipmentEffects"),
				"EquipmentController must express whether this peer applied its items' modifiers.");

			Match reset = Regex.Match(source,
				@"public override void ResetState\(bool asServer\)\s*\{(?<body>.*?)\n\t\t\}", RegexOptions.Singleline);
			LogAssert.IsTrue(reset.Success, "ResetState must be findable to check its gating.");

			string body = reset.Groups["body"].Value;
			int silent = body.IndexOf("ClearEquippedCharacterSilently", StringComparison.Ordinal);
			int clear = body.IndexOf("Clear();", StringComparison.Ordinal);

			LogAssert.IsTrue(silent >= 0,
				"ResetState must detach a non-simulating peer's items before Clear(), or Item.Destroy " +
				"raises OnUnequip and subtracts bonuses this peer never added.");
			LogAssert.IsTrue(clear > silent,
				"The silent detach must come BEFORE Clear(); afterwards the items are already destroyed " +
				"and the modifiers already gone.");
		}
		/// <summary>
		/// The attribute reconcile runs after the equipment's: equipping during reconcile runs ItemGenerator.ApplyAttributes →
		/// AddModifier, which the attribute snapshot must land on top of rather than under (the core pins the buffs' half).
		/// </summary>
		[Test]
		public void AttributeReconcile_RunsAfterEquipment()
		{
			int attributes = OrderOf<CharacterAttributeController>();
			int equipment = OrderOf<EquipmentController>();
			LogAssert.IsTrue(attributes > equipment,
				$"CharacterAttributeController.Order ({attributes}) must exceed EquipmentController.Order ({equipment}): " +
				"equipping during reconcile runs ItemGenerator.ApplyAttributes → AddModifier, which the " +
				"attribute snapshot must land on top of rather than under.");
		}

		#endregion

		#region Helpers

		private EquipmentController MakeEquipmentController(string name)
		{
			GameObject go = new GameObject(name);
			gameObjects.Add(go);
			EquipmentController controller = go.AddComponent<EquipmentController>();
			controller.OnAwake();
			controller.InitializeOnce(new MockCharacter(9));
			return controller;
		}
		private static void ApplyObservedSlot(EquipmentController controller, EquipmentObservedSlotBroadcast msg)
		{
			controller.ApplyObservedSlot(msg.Slot, msg.TemplateID, msg.Seed);
		}
		private static EquipmentObservedSlotBroadcast RoundTripEquipmentSlot(EquipmentObservedSlotBroadcast m)
		{
			Writer w = new Writer();
			w.WriteInt32(m.CharacterObjectID);
			w.WriteUInt8Unpacked(m.Slot);
			w.WriteInt32(m.TemplateID);
			w.WriteInt32(m.Seed);
			Reader r = new Reader(w.GetArraySegment(), null);
			return new EquipmentObservedSlotBroadcast
			{
				CharacterObjectID = r.ReadInt32(),
				Slot = r.ReadUInt8Unpacked(),
				TemplateID = r.ReadInt32(),
				Seed = r.ReadInt32(),
			};
		}
		private BaseItemTemplate MakeItemTemplate(string name)
		{
			ProofItemTemplate t = ScriptableObject.CreateInstance<ProofItemTemplate>();
			t.name = name;
			t.AddToCache(t.name);
			assets.Add(t);
			return t;
		}
		/// <summary>Reads a controller's pipeline order without a live NetworkManager (an instance on a throwaway object).</summary>
		private static int OrderOf<T>() where T : MonoBehaviour, IPredictableController
		{
			GameObject go = new GameObject("OrderProbe_" + typeof(T).Name);
			try
			{
				return go.AddComponent<T>().Order;
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		/// <summary>An equippable, generatable template with nothing else on it.</summary>
		private sealed class FixArmorTemplate : EquippableItemTemplate { }
		private sealed class ProofItemTemplate : BaseItemTemplate { }
		/// <summary>
		/// Counts requests for the attribute controller, which is the first thing
		/// <c>ItemGenerator.ApplyAttributes</c> does and therefore the cheapest evidence that it ran.
		/// </summary>
		private sealed class CountingCharacter : ICharacter
		{
			public int AttributeControllerLookups;

			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour
			{
				if (typeof(T) == typeof(ICharacterAttributeController))
				{
					++AttributeControllerLookups;
				}
				control = null;
				return false;
			}

			public long ID { get; set; } = 1L;
			public string Name => "F2Character";
			public Transform Transform => null;
			public GameObject GameObject => null;
			public Collider Collider { get; set; }
			public FishNet.Connection.NetworkConnection Owner => null;
			public FishNet.Object.NetworkObject NetworkObject => null;
			public FishNet.Managing.Predicting.PredictionManager PredictionManager => null;
			public System.Collections.Generic.HashSet<FishNet.Connection.NetworkConnection> Observers { get; }
				= new System.Collections.Generic.HashSet<FishNet.Connection.NetworkConnection>();
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
			public void Invoke(System.Collections.Generic.List<Trigger> triggers, EventData eventData) { }
		}
		private sealed class MockCharacter : ICharacter
		{
			public MockCharacter(long id = 77) => ID = id;
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
			public void EnableFlags(CharacterFlags flags) => Flags |= (int)flags;
			public void DisableFlags(CharacterFlags flags) => Flags &= ~(int)flags;
			public bool IsFlagged(CharacterFlags flags) => (Flags & (int)flags) != 0;
			public void RegisterCharacterBehaviour(ICharacterBehaviour b) { }
			public void UnregisterCharacterBehaviour(ICharacterBehaviour b) { }
			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour { control = null; return false; }
			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}

		#endregion
	}
}
