using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Managing.Timing;
using FishNet.Object;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Behavioural proofs for predicted equipment: a request rides the replicate, a snapshot older
	/// than the request restores the earlier state and the replay re-applies it, and a snapshot at
	/// or past the request is the verdict.
	/// </summary>
	/// <remarks>
	/// These run the controller's replicate and reconcile entry points directly, on an unspawned
	/// controller, with the peer's role passed in — which is exactly why those seams exist. The
	/// sequence each test walks is the one FishNet produces: the owner applies its input on tick T,
	/// then receives the state for an earlier tick (queued behind the input by the interpolation
	/// buffer), restores it, and replays T. Before equipment was input, the "restore" half of that
	/// sequence had nothing to replay, and undid the equip for good.
	/// </remarks>
	[TestFixture]
	public class PredictedEquipmentTests
	{
		private const byte PrimarySocket = (byte)ItemSlot.Primary;

		private readonly List<GameObject> gameObjects = new List<GameObject>();
		private readonly List<UnityEngine.Object> assets = new List<UnityEngine.Object>();

		private MockCharacter character;
		private InventoryController inventory;
		private BankController bank;
		private EquipmentController equipment;
		private EquippableItemTemplate sword;

		[SetUp]
		public void SetUp()
		{
			EquipmentController.ServerRequestValidator = null;

			character = new MockCharacter(42);

			GameObject go = new GameObject("PredictedEquipmentProbe");
			gameObjects.Add(go);

			inventory = go.AddComponent<InventoryController>();
			inventory.OnAwake();
			inventory.InitializeOnce(character);
			character.Register<IInventoryController>(inventory);

			bank = go.AddComponent<BankController>();
			bank.OnAwake();
			bank.InitializeOnce(character);
			character.Register<IBankController>(bank);

			equipment = go.AddComponent<EquipmentController>();
			equipment.OnAwake();
			equipment.InitializeOnce(character);
			character.Register<IEquipmentController>(equipment);

			ProofEquippableTemplate t = ScriptableObject.CreateInstance<ProofEquippableTemplate>();
			t.name = "PredictedEquipment_Sword";
			t.Slot = ItemSlot.Primary;
			t.MaxStackSize = 1;
			t.AddToCache(t.name);
			assets.Add(t);
			sword = t;
		}

		[TearDown]
		public void TearDown()
		{
			EquipmentController.ServerRequestValidator = null;
			foreach (UnityEngine.Object asset in assets) UnityEngine.Object.DestroyImmediate(asset);
			foreach (GameObject go in gameObjects) UnityEngine.Object.DestroyImmediate(go);
			assets.Clear();
			gameObjects.Clear();
		}

		// ── Packing ────────────────────────────────────────────────────────

		[Test]
		public void EquipmentReplicateInput_RoundTripsEveryDefinedRequest()
		{
			foreach (ItemSlot socket in Enum.GetValues(typeof(ItemSlot)))
			{
				foreach (InventoryType container in new[] { InventoryType.Inventory, InventoryType.Bank })
				{
					foreach (EquipmentRequestKind kind in new[] { EquipmentRequestKind.Equip, EquipmentRequestKind.Unequip })
					{
						LogAssert.IsTrue(EquipmentReplicateInput.TryPack(kind, container, socket, out byte packed),
							$"{kind}/{container}/{socket} must pack.");
						LogAssert.IsTrue(packed != 0, "a real request never packs to the no-request value");
						LogAssert.IsTrue(EquipmentReplicateInput.TryUnpack(packed, out EquipmentRequestKind k, out InventoryType c, out ItemSlot s),
							$"{kind}/{container}/{socket} must unpack.");
						LogAssert.AreEqual(kind, k, "kind survives the byte");
						LogAssert.AreEqual(container, c, "container survives the byte");
						LogAssert.AreEqual(socket, s, "socket survives the byte");
					}
				}
			}

			LogAssert.IsFalse(EquipmentReplicateInput.TryPack(EquipmentRequestKind.None, InventoryType.Inventory, ItemSlot.Head, out _),
				"no-request is not a packable request");
			LogAssert.IsFalse(EquipmentReplicateInput.TryPack(EquipmentRequestKind.Equip, InventoryType.Equipment, ItemSlot.Head, out _),
				"equipment-to-equipment is not a move the protocol has");
			LogAssert.IsFalse(EquipmentReplicateInput.TryUnpack(0, out _, out _, out _),
				"zero decodes to nothing asked");
		}

		[Test]
		public void ReplicateSerializers_CarryTheRequest()
		{
			CharacterReplicateData next = default;
			LogAssert.IsTrue(EquipmentReplicateInput.TryPack(EquipmentRequestKind.Equip, InventoryType.Bank, ItemSlot.Feet, out next.EquipmentRequest), "pack");
			next.EquipmentIndex = 17;

			FishNet.Serializing.Writer full = new FishNet.Serializing.Writer();
			full.WriteCharacterReplicateData(next);
			FishNet.Serializing.Reader fullReader = new FishNet.Serializing.Reader(full.GetArraySegment(), null);
			CharacterReplicateData back = fullReader.ReadCharacterReplicateData();
			LogAssert.AreEqual(next.EquipmentRequest, back.EquipmentRequest, "full serializer: request");
			LogAssert.AreEqual(next.EquipmentIndex, back.EquipmentIndex, "full serializer: index");
			LogAssert.AreEqual(0, fullReader.Remaining, "full serializer: no trailing bytes");
		}

		// ── Prediction ─────────────────────────────────────────────────────

		[Test]
		public void PredictedEquip_StaleSnapshotIsRestored_AndTheReplayReappliesIt()
		{
			Item a = PutInInventory(100, 3);

			CharacterReplicateData input = QueueAndPopulate(() => equipment.RequestEquip(a, 3, InventoryType.Inventory, ItemSlot.Primary), tick: 50);
			LogAssert.IsTrue(equipment.ApplyEquipmentInput(ref input, authoritative: false, owner: true, replayed: false), "the owner applies its own request on tick 50");
			AssertSocket(a, "after the predicted equip");
			LogAssert.IsTrue(inventory.IsSlotEmpty(3), "the source slot is vacated");
			LogAssert.IsTrue(equipment.HasPredictedMove(PrimarySocket), "the move is recorded until a snapshot rules on it");

			// The snapshot for tick 48 arrives: the server had not seen the request yet.
			equipment.RestoreFromReconcile(null, reconcileTick: 48);
			LogAssert.IsTrue(equipment.IsSlotEmpty(PrimarySocket), "a snapshot before the request empties the socket");
			LogAssert.IsTrue(ReferenceEquals(Get(inventory, 3), a), "the item goes back to the index the request took it from, not the first free slot");
			LogAssert.IsTrue(equipment.HasPredictedMove(PrimarySocket), "a snapshot before the request does not settle it");

			// FishNet now replays tick 50 with the same input.
			LogAssert.IsTrue(equipment.ApplyEquipmentInput(ref input, authoritative: false, owner: true, replayed: true), "the replay re-applies the request");
			AssertSocket(a, "after the replay");
			LogAssert.IsTrue(inventory.IsSlotEmpty(3), "the source slot is vacated again");

			// The snapshot for tick 52 confirms it.
			equipment.RestoreFromReconcile(new[] { Entry(a) }, reconcileTick: 52);
			AssertSocket(a, "after the confirming snapshot");
			LogAssert.IsTrue(inventory.IsSlotEmpty(3), "nothing was cloned into the inventory");
			LogAssert.IsFalse(equipment.HasPredictedMove(PrimarySocket), "a snapshot at or past the request settles it");
		}

		[Test]
		public void PredictedEquip_RefusedByTheServer_ReturnsTheItemToItsOrigin()
		{
			Item a = PutInInventory(100, 3);
			CharacterReplicateData input = QueueAndPopulate(() => equipment.RequestEquip(a, 3, InventoryType.Inventory, ItemSlot.Primary), tick: 50);
			LogAssert.IsTrue(equipment.ApplyEquipmentInput(ref input, false, true, false), "predicted");

			// Tick 52's snapshot still shows the socket empty: the server refused.
			equipment.RestoreFromReconcile(null, reconcileTick: 52);
			LogAssert.IsTrue(equipment.IsSlotEmpty(PrimarySocket), "the socket stays empty");
			LogAssert.IsTrue(ReferenceEquals(Get(inventory, 3), a), "the item is back where it came from");
			LogAssert.IsFalse(equipment.HasPredictedMove(PrimarySocket), "the refusal settles the record");
			LogAssert.IsNull(a.Equippable.Character, "a refused equip leaves the item unequipped");
		}

		[Test]
		public void PredictedSwap_StaleSnapshotRestoresBothItems_AndTheReplayRedoesTheSwap()
		{
			Item worn = PutInSocket(200);
			Item a = PutInInventory(100, 3);

			CharacterReplicateData input = QueueAndPopulate(() => equipment.RequestEquip(a, 3, InventoryType.Inventory, ItemSlot.Primary), tick: 70);
			LogAssert.IsTrue(equipment.ApplyEquipmentInput(ref input, false, true, false), "predicted swap");
			AssertSocket(a, "after the swap");
			LogAssert.IsTrue(ReferenceEquals(Get(inventory, 3), worn), "the displaced item takes the source slot");

			// Tick 69: the server still has the old item worn.
			equipment.RestoreFromReconcile(new[] { Entry(worn) }, reconcileTick: 69);
			AssertSocket(worn, "after the stale snapshot");
			LogAssert.IsTrue(ReferenceEquals(Get(inventory, 3), a), "the incoming item is back at its origin, not somewhere else");
			LogAssert.AreEqual(1, inventory.FilledSlots(), "no clone of either item exists");

			LogAssert.IsTrue(equipment.ApplyEquipmentInput(ref input, false, true, true), "replay");
			AssertSocket(a, "after the replay");
			LogAssert.IsTrue(ReferenceEquals(Get(inventory, 3), worn), "displaced again");

			equipment.RestoreFromReconcile(new[] { Entry(a) }, reconcileTick: 71);
			AssertSocket(a, "confirmed");
			LogAssert.IsTrue(ReferenceEquals(Get(inventory, 3), worn), "confirmed displaced item");
			LogAssert.AreEqual(1, inventory.FilledSlots(), "still exactly one item in the inventory");
		}

		[Test]
		public void PredictedUnequipToBank_StaleSnapshotReequips_AndTheReplayUnequipsAgain()
		{
			Item a = PutInSocket(100);

			CharacterReplicateData input = QueueAndPopulate(() => equipment.RequestUnequip(ItemSlot.Primary, InventoryType.Bank), tick: 60);
			LogAssert.IsTrue(equipment.ApplyEquipmentInput(ref input, false, true, false), "predicted unequip");
			LogAssert.IsTrue(equipment.IsSlotEmpty(PrimarySocket), "socket emptied");
			LogAssert.IsTrue(ReferenceEquals(Get(bank, 0), a), "the item landed in the bank, not the inventory");
			LogAssert.AreEqual(0, inventory.FilledSlots(), "nothing touched the inventory");

			// Tick 58: the server still shows it worn.
			equipment.RestoreFromReconcile(new[] { Entry(a) }, reconcileTick: 58);
			AssertSocket(a, "re-equipped by the stale snapshot");
			LogAssert.IsTrue(bank.IsSlotEmpty(0), "taken back out of the bank slot it was put in");
			LogAssert.AreEqual(0, inventory.FilledSlots(), "the inventory is still untouched — the bug this replaces put it here");

			LogAssert.IsTrue(equipment.ApplyEquipmentInput(ref input, false, true, true), "replay");
			LogAssert.IsTrue(ReferenceEquals(Get(bank, 0), a), "back in the bank after the replay");

			equipment.RestoreFromReconcile(null, reconcileTick: 62);
			LogAssert.IsTrue(equipment.IsSlotEmpty(PrimarySocket), "confirmed empty");
			LogAssert.IsTrue(ReferenceEquals(Get(bank, 0), a), "confirmed in the bank");
			LogAssert.IsFalse(equipment.HasPredictedMove(PrimarySocket), "settled");

			// The server chose a different bank slot; the destination message corrects it by identity.
			equipment.ApplyUnequipDestination(100, InventoryType.Bank, 5);
			LogAssert.IsTrue(bank.IsSlotEmpty(0), "moved off the guessed slot");
			LogAssert.IsTrue(ReferenceEquals(Get(bank, 5), a), "onto the slot the server named");
		}

		[Test]
		public void TheServerAppliesTheSameInput_AndReportsTheChange()
		{
			Item a = PutInInventory(100, 3);
			equipment.ServerAuthorityForTests = true;

			EquipmentChange? reported = null;
			equipment.OnServerEquipmentChanged += (c, change) => reported = change;

			CharacterReplicateData input = QueueAndPopulate(() => equipment.RequestEquip(a, 3, InventoryType.Inventory, ItemSlot.Primary), tick: 50);
			LogAssert.IsTrue(equipment.ApplyEquipmentInput(ref input, authoritative: true, owner: false, replayed: false), "the server applies the replicated request");
			AssertSocket(a, "server socket");
			LogAssert.IsTrue(reported.HasValue, "the persistence hook hears about it");
			LogAssert.AreEqual(EquipmentRequestKind.Equip, reported.Value.Kind, "kind");
			LogAssert.AreEqual(3, reported.Value.ContainerIndex, "the source index travels with the change");
			LogAssert.IsTrue(ReferenceEquals(reported.Value.Item, a), "the item travels with the change");
			LogAssert.IsFalse(equipment.HasPredictedMove(PrimarySocket), "the server records no prediction");
		}

		[Test]
		public void TheServerGateRefusesAndLeavesEverythingWhereItWas()
		{
			Item a = PutInInventory(100, 3);
			equipment.ServerAuthorityForTests = true;
			EquipmentController.ServerRequestValidator = (c, container) => false;

			CharacterReplicateData input = QueueAndPopulate(() => equipment.RequestEquip(a, 3, InventoryType.Inventory, ItemSlot.Primary), tick: 50);
			LogAssert.IsFalse(equipment.ApplyEquipmentInput(ref input, authoritative: true, owner: false, replayed: false), "refused by the gate");
			LogAssert.IsTrue(equipment.IsSlotEmpty(PrimarySocket), "socket untouched");
			LogAssert.IsTrue(ReferenceEquals(Get(inventory, 3), a), "source untouched");
		}

		[Test]
		public void AnItemWithoutAnIdentityCannotBeRequested_OrApplied()
		{
			Item unwritten = new Item(0, 0, sword, 1);
			LogAssert.IsTrue(inventory.SetItemSlot(unwritten, 3), "placed");

			LogAssert.IsFalse(equipment.RequestEquip(unwritten, 3, InventoryType.Inventory, ItemSlot.Primary),
				"an item the database has not written cannot be requested");
			LogAssert.IsFalse(equipment.HasQueuedRequest, "nothing queued");

			// A crafted input that names it anyway is refused on both peers.
			CharacterReplicateData input = default;
			LogAssert.IsTrue(EquipmentReplicateInput.TryPack(EquipmentRequestKind.Equip, InventoryType.Inventory, ItemSlot.Primary, out input.EquipmentRequest), "pack");
			input.EquipmentIndex = 3;
			input.SetTick(50);
			LogAssert.IsFalse(equipment.ApplyEquipmentInput(ref input, authoritative: true, owner: false, replayed: false), "server refuses");
			LogAssert.IsFalse(equipment.ApplyEquipmentInput(ref input, authoritative: false, owner: true, replayed: false), "owner refuses");
			LogAssert.IsTrue(ReferenceEquals(Get(inventory, 3), unwritten), "still in the inventory");
		}

		[Test]
		public void OneRequestPerTick_AndTheQueueEmptiesIntoTheInput()
		{
			Item a = PutInInventory(100, 3);
			Item worn = PutInSocket(200);

			int displaced = 0;
			equipment.OnRequestResolved += (kind, socket, container, index, applied) => { if (!applied) displaced++; };

			LogAssert.IsTrue(equipment.RequestUnequip(ItemSlot.Primary, InventoryType.Inventory), "first request queues");
			LogAssert.IsTrue(equipment.RequestEquip(a, 3, InventoryType.Inventory, ItemSlot.Primary), "a second request in the same tick replaces it");
			LogAssert.AreEqual(1, displaced, "the displaced request is reported as not applied so its slots unlock");

			CharacterReplicateData input = default;
			equipment.PopulateInput(ref input);
			LogAssert.IsTrue(input.EquipmentRequest != 0, "the queued request is written into the replicate");
			LogAssert.IsTrue(EquipmentReplicateInput.TryUnpack(input.EquipmentRequest, out EquipmentRequestKind kind, out _, out _) && kind == EquipmentRequestKind.Equip,
				"the last request is the one that rides");
			LogAssert.AreEqual((short)3, input.EquipmentIndex, "with its source index");
			LogAssert.IsFalse(equipment.HasQueuedRequest, "and the queue is empty");

			CharacterReplicateData idle = default;
			equipment.PopulateInput(ref idle);
			LogAssert.AreEqual((byte)0, idle.EquipmentRequest, "an idle tick carries no request");
		}

		// ── Helpers ────────────────────────────────────────────────────────

		private Item PutInInventory(long id, int slot)
		{
			Item item = new Item(id, 0, sword, 1);
			LogAssert.IsTrue(inventory.SetItemSlot(item, slot), $"seed inventory slot {slot}");
			return item;
		}

		private Item PutInSocket(long id)
		{
			Item item = new Item(id, 0, sword, 1);
			LogAssert.IsTrue(equipment.SetItemSlot(item, PrimarySocket), "seed socket");
			item.Equippable.Equip(character);
			return item;
		}

		private CharacterReplicateData QueueAndPopulate(Func<bool> request, uint tick)
		{
			LogAssert.IsTrue(request(), "the request must queue");
			CharacterReplicateData input = default;
			equipment.PopulateInput(ref input);
			input.SetTick(tick);
			LogAssert.IsTrue(input.EquipmentRequest != 0, "the request must be in the input");
			return input;
		}

		private EquipmentReconcileEntry Entry(Item item)
		{
			return new EquipmentReconcileEntry
			{
				TemplateID = sword.ID,
				Slot = PrimarySocket,
				Seed = 0,
				ItemID = item.ID,
			};
		}

		private static Item Get(IItemContainer container, int slot)
		{
			container.TryGetItem(slot, out Item item);
			return item;
		}

		private void AssertSocket(Item expected, string context)
		{
			LogAssert.IsTrue(equipment.TryGetItem(PrimarySocket, out Item actual) && ReferenceEquals(actual, expected),
				$"{context}: the socket must hold the expected instance");
			LogAssert.IsTrue(ReferenceEquals(expected.Equippable.Character, character),
				$"{context}: the worn item must know its character");
		}

		private sealed class ProofEquippableTemplate : EquippableItemTemplate { }

		private sealed class MockCharacter : ICharacter
		{
			private readonly Dictionary<Type, ICharacterBehaviour> behaviours = new Dictionary<Type, ICharacterBehaviour>();

			public MockCharacter(long id) => ID = id;
			public void Register<T>(T behaviour) where T : class, ICharacterBehaviour => behaviours[typeof(T)] = behaviour;

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
			public int Flags { get; set; } = 1 << (int)CharacterFlags.IsLoaded;
			public WorldLabel CharacterNameLabel { get; set; }
			public WorldLabel CharacterGuildLabel { get; set; }
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
			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour
			{
				if (behaviours.TryGetValue(typeof(T), out ICharacterBehaviour found) && found is T typed)
				{
					control = typed;
					return true;
				}
				control = null;
				return false;
			}
			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}
	}
}
