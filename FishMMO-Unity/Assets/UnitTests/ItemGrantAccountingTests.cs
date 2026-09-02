using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that <c>ItemContainer.TryAddItem</c> reports exactly the rows a grant changed.
	/// </summary>
	/// <remarks>
	/// The modified list is turned straight into persistence rows and set-slot broadcasts by every
	/// caller. It used to list the donor after a PARTIAL merge, which was right only when the
	/// leftover then went into an empty slot — and even then it listed it twice. When the leftover
	/// merged into a second stack instead, the list carried a donor with amount zero and slot -1,
	/// which was written as a row the load path could not place and turned into a phantom item on
	/// the next login.
	/// </remarks>
	[TestFixture]
	public class ItemGrantAccountingTests
	{
		private readonly List<GameObject> gameObjects = new List<GameObject>();
		private readonly List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
		private InventoryController inventory;
		private BaseItemTemplate potion;

		[SetUp]
		public void SetUp()
		{
			GameObject go = new GameObject("ItemGrantAccountingProbe");
			gameObjects.Add(go);
			inventory = go.AddComponent<InventoryController>();
			inventory.OnAwake();
			inventory.InitializeOnce(new MockCharacter(7));

			ProofItemTemplate t = ScriptableObject.CreateInstance<ProofItemTemplate>();
			t.name = "ItemGrantAccounting_Potion";
			t.MaxStackSize = 10;
			t.AddToCache(t.name);
			assets.Add(t);
			potion = t;
		}

		[TearDown]
		public void TearDown()
		{
			foreach (UnityEngine.Object asset in assets) UnityEngine.Object.DestroyImmediate(asset);
			foreach (GameObject go in gameObjects) UnityEngine.Object.DestroyImmediate(go);
			assets.Clear();
			gameObjects.Clear();
		}

		[Test]
		public void ADonorMergedAcrossTwoStacks_ReportsOnlyTheStacks()
		{
			Item first = new Item(1, 0, potion, 7);
			Item second = new Item(2, 0, potion, 5);
			LogAssert.IsTrue(inventory.SetItemSlot(first, 0), "seed first stack");
			LogAssert.IsTrue(inventory.SetItemSlot(second, 1), "seed second stack");

			Item donor = new Item(0, 0, potion, 8);
			LogAssert.IsTrue(inventory.TryAddItem(donor, out List<Item> modified), "the whole donor fits across the two stacks");

			LogAssert.AreEqual(10u, first.Stackable.Amount, "first stack filled");
			LogAssert.AreEqual(10u, second.Stackable.Amount, "second stack took the rest");
			LogAssert.AreEqual(0u, donor.Stackable.Amount, "donor consumed");
			LogAssert.AreEqual(2, modified.Count, "exactly the two stacks are reported");
			LogAssert.IsFalse(modified.Contains(donor), "a donor with no slot must never become a row");
			LogAssert.AreEqual(2, inventory.FilledSlots(), "no phantom slot was created");
		}

		[Test]
		public void ADonorPartlyMergedThenPlaced_IsReportedOnce_WithItsSlot()
		{
			Item first = new Item(1, 0, potion, 7);
			LogAssert.IsTrue(inventory.SetItemSlot(first, 0), "seed stack");

			Item donor = new Item(0, 0, potion, 8);
			LogAssert.IsTrue(inventory.TryAddItem(donor, out List<Item> modified), "merge then place");

			LogAssert.AreEqual(10u, first.Stackable.Amount, "stack filled");
			LogAssert.AreEqual(5u, donor.Stackable.Amount, "the leftover stays on the donor");
			LogAssert.AreEqual(1, donor.Slot, "and the donor now has a slot of its own");
			LogAssert.AreEqual(2, modified.Count, "the stack and the placed donor");
			LogAssert.AreEqual(1, modified.FindAll(i => ReferenceEquals(i, donor)).Count, "the donor is reported exactly once");
			LogAssert.IsTrue(modified.Contains(first), "the stack is reported");
		}

		[Test]
		public void ADonorFullyMergedIntoOneStack_ReportsTheStackAlone()
		{
			Item first = new Item(1, 0, potion, 2);
			LogAssert.IsTrue(inventory.SetItemSlot(first, 0), "seed stack");

			Item donor = new Item(0, 0, potion, 3);
			LogAssert.IsTrue(inventory.TryAddItem(donor, out List<Item> modified), "merge");

			LogAssert.AreEqual(1, modified.Count, "one row changed");
			LogAssert.IsTrue(ReferenceEquals(modified[0], first), "and it is the stack");
			LogAssert.AreEqual(1, inventory.FilledSlots(), "nothing else was placed");
		}

		private sealed class ProofItemTemplate : BaseItemTemplate { }

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
			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour { control = null; return false; }
			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}
	}
}
