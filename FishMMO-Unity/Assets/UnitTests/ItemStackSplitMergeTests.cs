using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for splitting a stack in two and merging two stacks into one (issue #198).
	/// </summary>
	/// <remarks>
	/// <para>
	/// The property every test here comes back to is the one the issue asked for: a split is one
	/// stack becoming two, and the total across the containers involved is identical before and
	/// after — on success, and on every refusal. A refusal must leave the original untouched
	/// rather than destroy the remainder, so each refused case asserts the source amount as well
	/// as the total.
	/// </para>
	/// <para>
	/// The containers are the real <see cref="InventoryController"/> and <see cref="BankController"/>
	/// on a throwaway GameObject, as the equipment proofs do it, so slot locks, the slot-updated
	/// events and cross-container placement are all the production code paths.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ItemStackSplitMergeTests
	{
		private const uint MaxStack = 10;

		private readonly List<GameObject> gameObjects = new List<GameObject>();
		private readonly List<UnityEngine.Object> assets = new List<UnityEngine.Object>();

		private InventoryController inventory;
		private BankController bank;
		private StackableTestTemplate arrows;
		private StackableTestTemplate bolts;
		private SingleTestTemplate sword;

		private long nextID = 1000;

		[SetUp]
		public void SetUp()
		{
			GameObject go = new GameObject("ItemStackSplitMergeProbe");
			gameObjects.Add(go);

			inventory = go.AddComponent<InventoryController>();
			inventory.OnAwake();

			bank = go.AddComponent<BankController>();
			bank.OnAwake();

			arrows = ScriptableObject.CreateInstance<StackableTestTemplate>();
			arrows.MaxStackSize = MaxStack;
			arrows.Generate = false;
			arrows.name = "SplitMerge_Arrows";
			arrows.AddToCache(arrows.name);
			assets.Add(arrows);

			bolts = ScriptableObject.CreateInstance<StackableTestTemplate>();
			bolts.MaxStackSize = MaxStack;
			bolts.Generate = false;
			bolts.name = "SplitMerge_Bolts";
			bolts.AddToCache(bolts.name);
			assets.Add(bolts);

			sword = ScriptableObject.CreateInstance<SingleTestTemplate>();
			sword.MaxStackSize = 1;
			sword.Generate = false;
			sword.name = "SplitMerge_Sword";
			sword.AddToCache(sword.name);
			assets.Add(sword);
		}

		[TearDown]
		public void TearDown()
		{
			foreach (UnityEngine.Object asset in assets) UnityEngine.Object.DestroyImmediate(asset);
			foreach (GameObject go in gameObjects) UnityEngine.Object.DestroyImmediate(go);
			assets.Clear();
			gameObjects.Clear();
		}

		private class StackableTestTemplate : BaseItemTemplate { }
		private class SingleTestTemplate : BaseItemTemplate { }

		/// <summary>Places an identified stack in a slot, as a loaded item would be.</summary>
		private Item Put(IItemContainer container, int slot, BaseItemTemplate template, uint amount)
		{
			Item item = new Item(nextID++, 0, template, amount);
			LogAssert.IsTrue(container.SetItemSlot(item, slot), $"precondition: slot {slot} accepts the item");
			return item;
		}

		private static ulong Total(params IItemContainer[] containers)
		{
			ulong total = 0;
			foreach (IItemContainer container in containers)
			{
				for (int i = 0; i < container.Items.Count; ++i)
				{
					Item item = container.Items[i];
					if (item == null) continue;
					total += item.IsStackable ? item.Stackable.Amount : 1u;
				}
			}
			return total;
		}

		private static Item Get(IItemContainer container, int slot)
		{
			container.TryGetItem(slot, out Item item);
			return item;
		}

		// ── Split ───────────────────────────────────────────────────────────

		[Test]
		public void Split_IntoAnEmptySlot_CreatesANewInstanceAndConservesTheTotal()
		{
			Item stack = Put(inventory, 0, arrows, 9);
			ulong before = Total(inventory);

			bool split = ItemStackTransfer.TrySplit(inventory, 0, inventory, 1, 4, out Item source, out Item taken, out bool created);

			LogAssert.IsTrue(split, "4 off a stack of 9 into an empty slot must succeed");
			LogAssert.IsTrue(created, "an empty destination gets a new instance");
			LogAssert.IsTrue(ReferenceEquals(source, stack), "the source is the original instance");
			LogAssert.IsFalse(ReferenceEquals(taken, stack), "the split half is a different instance");
			LogAssert.AreEqual(5u, stack.Stackable.Amount, "the source keeps 9 - 4");
			LogAssert.AreEqual(4u, taken.Stackable.Amount, "the split half carries 4");
			LogAssert.AreEqual(0L, taken.ID, "the split half is not a row yet");
			LogAssert.AreEqual(1, taken.Slot, "the split half knows its slot");
			LogAssert.IsTrue(ReferenceEquals(Get(inventory, 1), taken), "the split half sits in the destination");
			LogAssert.IsTrue(ReferenceEquals(Get(inventory, 0), stack), "the source stays in its slot");
			LogAssert.AreEqual(before, Total(inventory), "the total survives the split");
		}

		[Test]
		public void Split_AcrossContainers_LandsInTheOtherContainerAndConservesTheTotal()
		{
			Item stack = Put(inventory, 3, arrows, 7);
			ulong before = Total(inventory, bank);

			bool split = ItemStackTransfer.TrySplit(inventory, 3, bank, 10, 2, out _, out Item taken, out bool created);

			LogAssert.IsTrue(split, "a split can cross into the bank");
			LogAssert.IsTrue(created, "a new instance in the bank");
			LogAssert.AreEqual(5u, stack.Stackable.Amount, "5 stay in the inventory");
			LogAssert.IsTrue(ReferenceEquals(Get(bank, 10), taken), "2 arrive in the bank");
			LogAssert.AreEqual(2u, taken.Stackable.Amount, "and it is exactly 2");
			LogAssert.AreEqual(before, Total(inventory, bank), "the total across both survives");
		}

		[Test]
		public void Split_OntoAMatchingStackWithRoom_PoursAcrossWithoutANewInstance()
		{
			Item stack = Put(inventory, 0, arrows, 8);
			Item receiver = Put(inventory, 5, arrows, 3);
			ulong before = Total(inventory);

			bool split = ItemStackTransfer.TrySplit(inventory, 0, inventory, 5, 6, out _, out Item destination, out bool created);

			LogAssert.IsTrue(split, "6 off 8 onto a stack of 3 with a cap of 10 fits exactly");
			LogAssert.IsFalse(created, "no new instance when the destination already holds a matching stack");
			LogAssert.IsTrue(ReferenceEquals(destination, receiver), "the destination is the existing stack");
			LogAssert.AreEqual(2u, stack.Stackable.Amount, "the source keeps 8 - 6");
			LogAssert.AreEqual(9u, receiver.Stackable.Amount, "the receiver holds 3 + 6");
			LogAssert.AreEqual(before, Total(inventory), "the total survives");
		}

		[Test]
		public void Split_OntoAMatchingStackWithoutRoom_IsRefusedAndNothingMoves()
		{
			/* Refused rather than partly filled: the server's answer is 'done as asked' or
			 * 'nothing happened', never 'some of it'. */
			Item stack = Put(inventory, 0, arrows, 8);
			Item receiver = Put(inventory, 5, arrows, 7);
			ulong before = Total(inventory);

			bool split = ItemStackTransfer.TrySplit(inventory, 0, inventory, 5, 4, out _, out _, out _);

			LogAssert.IsFalse(split, "4 onto a stack with room for 3 must be refused");
			LogAssert.AreEqual(8u, stack.Stackable.Amount, "the source is untouched");
			LogAssert.AreEqual(7u, receiver.Stackable.Amount, "the receiver is untouched");
			LogAssert.AreEqual(before, Total(inventory), "the total is untouched");
		}

		[Test]
		public void Split_OntoADifferentItem_IsRefusedAndNothingMoves()
		{
			Item stack = Put(inventory, 0, arrows, 8);
			Item other = Put(inventory, 5, bolts, 2);
			ulong before = Total(inventory);

			bool split = ItemStackTransfer.TrySplit(inventory, 0, inventory, 5, 4, out _, out _, out _);

			LogAssert.IsFalse(split, "a split cannot land on a different item");
			LogAssert.AreEqual(8u, stack.Stackable.Amount, "source untouched");
			LogAssert.AreEqual(2u, other.Stackable.Amount, "occupant untouched");
			LogAssert.IsTrue(ReferenceEquals(Get(inventory, 5), other), "occupant still in place");
			LogAssert.AreEqual(before, Total(inventory), "total untouched");
		}

		[Test]
		public void Split_OfZero_IsRefused()
		{
			Item stack = Put(inventory, 0, arrows, 8);

			LogAssert.IsFalse(ItemStackTransfer.TrySplit(inventory, 0, inventory, 1, 0, out _, out _, out _), "zero is not a split");
			LogAssert.AreEqual(8u, stack.Stackable.Amount, "source untouched");
			LogAssert.IsTrue(inventory.IsSlotEmpty(1), "nothing was placed");
		}

		[Test]
		public void Split_OfTheWholeStack_IsRefused_BecauseThatIsAMove()
		{
			Item stack = Put(inventory, 0, arrows, 8);

			LogAssert.IsFalse(ItemStackTransfer.IsValidSplitAmount(stack, 8), "the whole stack is not a split amount");
			LogAssert.IsFalse(ItemStackTransfer.TrySplit(inventory, 0, inventory, 1, 8, out _, out _, out _), "taking everything is a move, and the swap is the operation for that");
			LogAssert.AreEqual(8u, stack.Stackable.Amount, "source untouched");
			LogAssert.IsTrue(ReferenceEquals(Get(inventory, 0), stack), "source still in its slot");
			LogAssert.IsTrue(inventory.IsSlotEmpty(1), "nothing was placed");
		}

		[Test]
		public void Split_OfMoreThanTheStackHolds_IsRefused()
		{
			Item stack = Put(inventory, 0, arrows, 8);

			LogAssert.IsFalse(ItemStackTransfer.TrySplit(inventory, 0, inventory, 1, 50, out _, out _, out _), "more than exists cannot be taken");
			LogAssert.AreEqual(8u, stack.Stackable.Amount, "source untouched");
			LogAssert.IsTrue(inventory.IsSlotEmpty(1), "nothing was placed");
		}

		[Test]
		public void Split_OntoItsOwnSlot_IsRefused()
		{
			Item stack = Put(inventory, 0, arrows, 8);

			LogAssert.IsFalse(ItemStackTransfer.TrySplit(inventory, 0, inventory, 0, 3, out _, out _, out _), "a slot cannot receive a split of itself");
			LogAssert.AreEqual(8u, stack.Stackable.Amount, "source untouched");
		}

		[Test]
		public void Split_OfANonStackable_IsRefused()
		{
			Item blade = Put(inventory, 0, sword, 1);

			LogAssert.IsFalse(blade.IsStackable, "precondition: a MaxStackSize of 1 has no stackable component");
			LogAssert.IsFalse(ItemStackTransfer.TrySplit(inventory, 0, inventory, 1, 1, out _, out _, out _), "there is nothing to split");
			LogAssert.IsTrue(ReferenceEquals(Get(inventory, 0), blade), "the item is untouched");
			LogAssert.IsTrue(inventory.IsSlotEmpty(1), "nothing was placed");
		}

		[Test]
		public void Split_FromOrIntoALockedSlot_IsRefusedBeforeAnythingIsWritten()
		{
			/* A locked slot is mid-operation — a consumable being used, an item waiting on its
			 * identity. Both operations write two slots in sequence, so the lock has to be
			 * refused up front or the first write lands and the second does not. */
			Item stack = Put(inventory, 0, arrows, 8);

			inventory.LockSlot(0);
			LogAssert.IsFalse(ItemStackTransfer.TrySplit(inventory, 0, inventory, 1, 3, out _, out _, out _), "a locked source refuses");
			LogAssert.AreEqual(8u, stack.Stackable.Amount, "source untouched");
			LogAssert.IsTrue(inventory.IsSlotEmpty(1), "nothing placed");
			inventory.UnlockSlot(0);

			inventory.LockSlot(1);
			LogAssert.IsFalse(ItemStackTransfer.TrySplit(inventory, 0, inventory, 1, 3, out _, out _, out _), "a locked destination refuses");
			LogAssert.AreEqual(8u, stack.Stackable.Amount, "source untouched");
			LogAssert.IsTrue(inventory.IsSlotEmpty(1), "nothing placed");
			inventory.UnlockSlot(1);
		}

		[Test]
		public void Split_RaisesSlotUpdatedForBothSlots_SoTheViewSeesTheNewAmounts()
		{
			Put(inventory, 0, arrows, 8);
			var updated = new List<int>();
			inventory.OnSlotUpdated += (c, item, slot) => updated.Add(slot);

			LogAssert.IsTrue(ItemStackTransfer.TrySplit(inventory, 0, inventory, 4, 3, out _, out _, out _), "split");

			LogAssert.IsTrue(updated.Contains(0), "the source slot reports its new amount");
			LogAssert.IsTrue(updated.Contains(4), "the destination slot reports the split half");
		}

		[Test]
		public void SplitHalves_CanBeMergedBackTogether()
		{
			/* The pair, end to end: what a split strands, a merge recombines. */
			Item stack = Put(inventory, 0, arrows, 9);
			ulong before = Total(inventory);

			LogAssert.IsTrue(ItemStackTransfer.TrySplit(inventory, 0, inventory, 1, 4, out _, out Item half, out _), "split");
			LogAssert.IsTrue(ItemStackTransfer.TryMerge(inventory, 1, inventory, 0, out Item donor, out Item receiver, out bool emptied), "merge back");

			LogAssert.IsTrue(ReferenceEquals(donor, half), "the split half was the donor");
			LogAssert.IsTrue(ReferenceEquals(receiver, stack), "the original was the receiver");
			LogAssert.IsTrue(emptied, "the split half was fully absorbed");
			LogAssert.AreEqual(9u, stack.Stackable.Amount, "the original holds everything again");
			LogAssert.IsTrue(inventory.IsSlotEmpty(1), "the split half's slot is empty again");
			LogAssert.AreEqual(before, Total(inventory), "the total survived the round trip");
		}

		// ── Merge ───────────────────────────────────────────────────────────

		[Test]
		public void Merge_ThatFitsEntirely_EmptiesTheDonorSlot()
		{
			Item donor = Put(inventory, 0, arrows, 4);
			Item receiver = Put(inventory, 1, arrows, 3);
			ulong before = Total(inventory);

			LogAssert.IsTrue(ItemStackTransfer.CanMergeInto(receiver, donor), "a matching stack with room is a merge target");
			bool merged = ItemStackTransfer.TryMerge(inventory, 0, inventory, 1, out Item source, out Item destination, out bool emptied);

			LogAssert.IsTrue(merged, "4 into 3 with a cap of 10 fits");
			LogAssert.IsTrue(emptied, "the donor was fully absorbed");
			LogAssert.IsTrue(ReferenceEquals(source, donor) && ReferenceEquals(destination, receiver), "the instances are reported");
			LogAssert.AreEqual(7u, receiver.Stackable.Amount, "the receiver holds 3 + 4");
			LogAssert.AreEqual(0u, donor.Stackable.Amount, "the donor is empty");
			LogAssert.AreEqual(-1, donor.Slot, "the donor is out of the container");
			LogAssert.IsTrue(inventory.IsSlotEmpty(0), "the donor's slot is empty");
			LogAssert.AreEqual(before, Total(inventory), "the total survives");
		}

		[Test]
		public void Merge_ThatOverflows_LeavesTheRemainderOnTheDonor()
		{
			Item donor = Put(inventory, 0, arrows, 7);
			Item receiver = Put(inventory, 1, arrows, 8);
			ulong before = Total(inventory);

			bool merged = ItemStackTransfer.TryMerge(inventory, 0, inventory, 1, out _, out _, out bool emptied);

			LogAssert.IsTrue(merged, "a partial merge still reports success");
			LogAssert.IsFalse(emptied, "the donor keeps what did not fit");
			LogAssert.AreEqual(MaxStack, receiver.Stackable.Amount, "the receiver fills to the cap");
			LogAssert.AreEqual(5u, donor.Stackable.Amount, "the donor keeps 7 - 2");
			LogAssert.IsTrue(ReferenceEquals(Get(inventory, 0), donor), "the donor stays in its slot");
			LogAssert.AreEqual(before, Total(inventory), "the total survives");
		}

		[Test]
		public void Merge_AcrossContainers_ConservesTheTotal()
		{
			Item donor = Put(inventory, 2, arrows, 6);
			Item receiver = Put(bank, 40, arrows, 1);
			ulong before = Total(inventory, bank);

			LogAssert.IsTrue(ItemStackTransfer.TryMerge(inventory, 2, bank, 40, out _, out _, out bool emptied), "a deposit onto a matching stack merges");

			LogAssert.IsTrue(emptied, "all six fit");
			LogAssert.AreEqual(7u, receiver.Stackable.Amount, "the bank stack holds 1 + 6");
			LogAssert.IsTrue(inventory.IsSlotEmpty(2), "the inventory slot emptied");
			LogAssert.AreEqual(before, Total(inventory, bank), "the total across both survives");
		}

		[Test]
		public void Merge_IntoAFullStack_IsNotAMerge_SoTheDropSwaps()
		{
			Item donor = Put(inventory, 0, arrows, 4);
			Item receiver = Put(inventory, 1, arrows, MaxStack);

			LogAssert.IsFalse(ItemStackTransfer.CanMergeInto(receiver, donor), "a full receiver is not a merge target");
			LogAssert.IsFalse(ItemStackTransfer.TryMerge(inventory, 0, inventory, 1, out _, out _, out _), "and TryMerge agrees");
			LogAssert.AreEqual(4u, donor.Stackable.Amount, "donor untouched");
			LogAssert.AreEqual(MaxStack, receiver.Stackable.Amount, "receiver untouched");
		}

		[Test]
		public void Merge_OfAFullDonor_IsNotAMerge_BecauseItWouldBeASwapByAnotherName()
		{
			Item donor = Put(inventory, 0, arrows, MaxStack);
			Item receiver = Put(inventory, 1, arrows, 3);

			LogAssert.IsFalse(ItemStackTransfer.CanMergeInto(receiver, donor), "pouring a full stack into a partial one leaves the donor holding what the receiver held: a swap");
			LogAssert.IsFalse(ItemStackTransfer.TryMerge(inventory, 0, inventory, 1, out _, out _, out _), "TryMerge agrees");
			LogAssert.AreEqual(MaxStack, donor.Stackable.Amount, "donor untouched");
			LogAssert.AreEqual(3u, receiver.Stackable.Amount, "receiver untouched");
		}

		[Test]
		public void Merge_OfDifferentItems_IsNotAMerge()
		{
			Item donor = Put(inventory, 0, arrows, 4);
			Item receiver = Put(inventory, 1, bolts, 3);

			LogAssert.IsFalse(ItemStackTransfer.CanMergeInto(receiver, donor), "different templates never merge");
			LogAssert.IsFalse(ItemStackTransfer.TryMerge(inventory, 0, inventory, 1, out _, out _, out _), "TryMerge agrees");
			LogAssert.AreEqual(4u, donor.Stackable.Amount, "donor untouched");
			LogAssert.AreEqual(3u, receiver.Stackable.Amount, "receiver untouched");
		}

		[Test]
		public void Merge_IntoAnEmptySlot_IsNotAMerge()
		{
			Item donor = Put(inventory, 0, arrows, 4);

			LogAssert.IsFalse(ItemStackTransfer.TryMerge(inventory, 0, inventory, 1, out _, out _, out _), "nothing to merge into; the drop is a move");
			LogAssert.AreEqual(4u, donor.Stackable.Amount, "donor untouched");
			LogAssert.IsTrue(ReferenceEquals(Get(inventory, 0), donor), "donor still in place");
		}

		[Test]
		public void Merge_InvolvingALockedSlot_IsRefusedBeforeAnythingIsWritten()
		{
			Item donor = Put(inventory, 0, arrows, 4);
			Item receiver = Put(inventory, 1, arrows, 3);

			inventory.LockSlot(1);
			LogAssert.IsFalse(ItemStackTransfer.TryMerge(inventory, 0, inventory, 1, out _, out _, out _), "a locked receiver refuses");
			LogAssert.AreEqual(4u, donor.Stackable.Amount, "donor untouched");
			LogAssert.AreEqual(3u, receiver.Stackable.Amount, "receiver untouched");
			inventory.UnlockSlot(1);

			inventory.LockSlot(0);
			LogAssert.IsFalse(ItemStackTransfer.TryMerge(inventory, 0, inventory, 1, out _, out _, out _), "a locked donor refuses");
			LogAssert.AreEqual(4u, donor.Stackable.Amount, "donor untouched");
			LogAssert.AreEqual(3u, receiver.Stackable.Amount, "receiver untouched");
			inventory.UnlockSlot(0);
		}

		[Test]
		public void Merge_RaisesSlotUpdatedForBothSlots()
		{
			Put(inventory, 0, arrows, 4);
			Put(inventory, 1, arrows, 3);
			var updated = new List<int>();
			inventory.OnSlotUpdated += (c, item, slot) => updated.Add(slot);

			LogAssert.IsTrue(ItemStackTransfer.TryMerge(inventory, 0, inventory, 1, out _, out _, out _), "merge");

			LogAssert.IsTrue(updated.Contains(0), "the emptied donor slot is reported");
			LogAssert.IsTrue(updated.Contains(1), "the receiver reports its new amount");
		}

		// ── The wiring around the primitive ─────────────────────────────────

		private const string ServerPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/CharacterInventory/CharacterInventorySystem.cs";
		private const string PanelPath =
			"Assets/Scripts/Client/GUI/World/ItemContainers/UITKItemGridPanel.cs";
		private const string EquipmentPanelPath =
			"Assets/Scripts/Client/GUI/World/Equipment/UITKEquipment.cs";
		private const string TrackerPath =
			"Assets/Scripts/Client/GUI/World/ItemContainers/ItemOperationTracker.cs";

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}

		private static string MethodBody(string relativePath, string signature, string nextSymbol)
		{
			string source = ReadSource(relativePath);
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"{relativePath} must still declare {signature}");
			int end = source.IndexOf(nextSymbol, start, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"{relativePath}: the end of {signature} must be locatable");
			return source.Substring(start, end - start);
		}

		[Test]
		public void TheServerLocksACreatedHalfUntilItsIdentityLands_AndNeverEchoesTheRequest()
		{
			/* A split half has id 0 until its row is written. Left unlocked, a second batch could
			 * capture it under id 0 and insert a second row for one item — the very thing the
			 * grant path locks against. And the client has never seen the half, so an echo of two
			 * indices could not tell it what the slot holds. */
			string body = MethodBody(ServerPath, "private void HandleSplitRequest", "#endregion");

			LogAssert.IsTrue(body.Contains("target.LockSlot(to)"), "the created half's slot must be locked");
			LogAssert.IsTrue(body.Contains("ItemStackTransfer.TrySplit("), "the split must go through the conserving primitive");
			LogAssert.IsTrue(body.Contains("BroadcastSlots(character, fromInventory") && body.Contains("BroadcastSlots(character, toInventory"),
				"both halves must be sent as set-slot messages");
			LogAssert.IsFalse(body.Contains("Broadcast(conn, msg"), "a split request must never be echoed");
			LogAssert.IsTrue(body.Contains("ValidateBankerSceneObject"), "the banker must be in range when the bank is either end");
		}

		[Test]
		public void EveryDropHandlerOffersTheMergeBeforeTheSwap()
		{
			/* Four places a stack can be dropped onto another: within the inventory, within the
			 * bank, and across in each direction. All four must consult the merge first, or a
			 * matching stack dropped in one of them swaps while the others merge. */
			string inventorySwap = MethodBody(ServerPath, "private void OnServerInventorySwapItemSlotsBroadcastReceived", "#region Equipment");
			string bankSwap = MethodBody(ServerPath, "private void OnServerBankSwapItemSlotsBroadcastReceived", "private bool ValidateBankerSceneObject");

			LogAssert.IsTrue(inventorySwap.Contains("\"InventoryMerge\""), "inventory-to-inventory merges");
			LogAssert.IsTrue(inventorySwap.Contains("\"BankWithdrawMerge\""), "bank-to-inventory merges");
			LogAssert.IsTrue(bankSwap.Contains("\"BankDepositMerge\""), "inventory-to-bank merges");
			LogAssert.IsTrue(bankSwap.Contains("\"BankMerge\""), "bank-to-bank merges");
		}

		[Test]
		public void TheClientRoutesAQuantityDragToTheSplitRequest_AndChecksTheDestinationFirst()
		{
			string drop = MethodBody(PanelPath, "protected void CompleteDropOntoSlot", "private void CompleteSplitOntoSlot");
			LogAssert.IsTrue(drop.Contains("dragObject.SplitAmount > 0"), "a drag carrying a quantity is a split, not a swap");

			string split = MethodBody(PanelPath, "private void CompleteSplitOntoSlot", "protected void CompleteUnequipInto");
			LogAssert.IsTrue(split.Contains("ItemStackTransfer.CanSplitOnto"), "the destination is checked against the server's rule");
			LogAssert.IsTrue(split.Contains("SendSplitRequest("), "and the split request is what goes out");
			LogAssert.IsTrue(split.Contains("ItemOperationTracker.Release("), "claiming the second end and failing must release the first");
		}

		[Test]
		public void TheWholeStackFromThePrompt_IsAnOrdinaryDrag()
		{
			/* Splitting the whole stack has a defined answer: it is a move. The prompt turns it
			 * into the same drag a press-and-drag would start, so it goes out as a swap. */
			string body = MethodBody(PanelPath, "private void OnSplitAmountEntered", "protected virtual void HandleSlotLeftClick");
			LogAssert.IsTrue(body.Contains("amount < held ? amount : 0u"), "the whole stack is carried as split amount 0");
			LogAssert.IsTrue(body.Contains("amount > held"), "more than the stack holds is refused out loud");
		}

		[Test]
		public void AnEquipmentSocketRefusesAQuantityDrag()
		{
			/* A split half is a quantity, not an item; nothing exists to equip until the server
			 * has made it. Acting on the drag would equip the whole stack it was taken from. */
			string body = MethodBody(EquipmentPanelPath, "private void CompleteDropOntoSlot(UITKDragObject dragObject, IEquipmentController", "IItemContainer sourceContainer = ResolveContainer");
			LogAssert.IsTrue(body.Contains("dragObject.SplitAmount > 0"), "the socket must check for a quantity drag");
		}

		[Test]
		public void ARefusedSplitReleasesBothSlotsOnTheClient()
		{
			string body = MethodBody(TrackerPath, "private static void OnItemOperationFailed", "if (msg.Reason == ItemOperationFailureReason.ServerBusy)");
			LogAssert.IsTrue(body.Contains("case ItemOperationType.InventorySplit:"), "an inventory split refusal is handled");
			LogAssert.IsTrue(body.Contains("case ItemOperationType.BankSplit:"), "a bank split refusal is handled");
		}
	}
}
