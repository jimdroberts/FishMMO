using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Conservation proofs for the in-memory half of a trade (issue #144), against real
	/// <see cref="InventoryController"/>s.
	/// </summary>
	/// <remarks>
	/// Two totals are pinned on every path: the quantity held across both bags, and the set
	/// of item identities. On success both are conserved and merely redistributed; on any
	/// refusal both bags are exactly as they were — no partial application, which would be
	/// item duplication rather than a lost trade.
	/// </remarks>
	[TestFixture]
	public class TradeExchangeTests
	{
		private const uint MaxStack = 10;

		private readonly List<GameObject> gameObjects = new List<GameObject>();
		private readonly List<Object> assets = new List<Object>();

		private InventoryController alice;
		private InventoryController bob;
		private StackableTestTemplate arrows;
		private StackableTestTemplate bolts;
		private SingleTestTemplate sword;

		private long nextID = 1000;

		private class StackableTestTemplate : BaseItemTemplate { }
		private class SingleTestTemplate : BaseItemTemplate { }

		[SetUp]
		public void SetUp()
		{
			GameObject a = new GameObject("TradeExchange_Alice");
			gameObjects.Add(a);
			alice = a.AddComponent<InventoryController>();
			alice.OnAwake();

			GameObject b = new GameObject("TradeExchange_Bob");
			gameObjects.Add(b);
			bob = b.AddComponent<InventoryController>();
			bob.OnAwake();

			arrows = Template<StackableTestTemplate>("TradeExchange_Arrows", MaxStack);
			bolts = Template<StackableTestTemplate>("TradeExchange_Bolts", MaxStack);
			sword = Template<SingleTestTemplate>("TradeExchange_Sword", 1);
		}

		[TearDown]
		public void TearDown()
		{
			foreach (Object asset in assets) Object.DestroyImmediate(asset);
			foreach (GameObject go in gameObjects) Object.DestroyImmediate(go);
			assets.Clear();
			gameObjects.Clear();
		}

		private T Template<T>(string name, uint maxStack) where T : BaseItemTemplate
		{
			T template = ScriptableObject.CreateInstance<T>();
			template.MaxStackSize = maxStack;
			template.Generate = false;
			template.name = name;
			template.AddToCache(template.name);
			assets.Add(template);
			return template;
		}

		/// <summary>Places an identified item in a slot, as a loaded item would be.</summary>
		private Item Put(IItemContainer container, int slot, BaseItemTemplate template, uint amount)
		{
			Item item = new Item(nextID++, 0, template, amount);
			LogAssert.IsTrue(container.SetItemSlot(item, slot), $"precondition: slot {slot} accepts the item");
			return item;
		}

		private static TradeOffer OfferOf(Item item, uint amount = 0)
		{
			uint held = item.IsStackable ? item.Stackable.Amount : 1u;
			return new TradeOffer(item.Slot, item.ID, item.Template.ID, 0, amount == 0 ? held : amount);
		}

		private static TradeExchange.Side Side(IItemContainer inventory, params TradeOffer[] offers)
		{
			return new TradeExchange.Side { Inventory = inventory, Offers = offers };
		}

		private static ulong Total(IItemContainer container, BaseItemTemplate template = null)
		{
			ulong total = 0;
			for (int i = 0; i < container.Items.Count; ++i)
			{
				Item item = container.Items[i];
				if (item == null || (template != null && item.Template != template)) continue;
				total += item.IsStackable ? item.Stackable.Amount : 1u;
			}
			return total;
		}

		private static HashSet<long> Identities(IItemContainer container)
		{
			var ids = new HashSet<long>();
			for (int i = 0; i < container.Items.Count; ++i)
			{
				Item item = container.Items[i];
				if (item != null && item.ID > 0) ids.Add(item.ID);
			}
			return ids;
		}

		private static Item At(IItemContainer container, int slot)
		{
			container.TryGetItem(slot, out Item item);
			return item;
		}

		/// <summary>Fills every empty slot so a receiver has no room.</summary>
		private void Fill(IItemContainer container, BaseItemTemplate with)
		{
			for (int i = 0; i < container.Items.Count; ++i)
			{
				if (container.IsSlotEmpty(i))
				{
					Put(container, i, with, 1);
				}
			}
		}

		// ── Whole moves ─────────────────────────────────────────────────────────────────────

		[Test]
		public void AWholeItem_CrossesWithItsIdentity_AndIsListedChangedByTheReceiverOnly()
		{
			Item swordItem = Put(alice, 0, sword, 1);
			long id = swordItem.ID;

			TradeExchange.Side a = Side(alice, OfferOf(swordItem));
			TradeExchange.Side b = Side(bob);

			LogAssert.IsTrue(TradeExchange.TryApply(a, b, out TradeExchange.Failure failure), $"the exchange applies ({failure})");

			LogAssert.IsNull(At(alice, 0), "Alice's slot is empty");
			LogAssert.IsTrue(bob.ContainsItem(sword), "Bob holds the sword");
			LogAssert.IsTrue(Identities(bob).Contains(id), "under the same identity");
			LogAssert.AreEqual(id, swordItem.ID, "the instance kept its id");
			LogAssert.IsTrue(swordItem.Slot >= 0, "and has a slot in Bob's bag");

			LogAssert.AreEqual(1, b.Changed.Count, "Bob's write carries the moved item");
			LogAssert.AreSame(swordItem, b.Changed[0], "as the same instance");
			LogAssert.AreEqual(0, a.Removed.Count, "Alice's write does NOT delete it: the row is re-owned, not replaced");
			LogAssert.AreEqual(0, a.Changed.Count, "and has nothing to upsert");
			LogAssert.AreEqual(1, a.EmptiedSlots.Count, "Alice's client is told slot 0 emptied");
			LogAssert.AreEqual(0, a.EmptiedSlots[0], "slot 0");
			LogAssert.AreEqual(0, b.UnidentifiedPlaced.Count, "nothing new was minted");
		}

		[Test]
		public void BothDirections_SwapItems_AndConserveEverything()
		{
			Item swordItem = Put(alice, 0, sword, 1);
			Item arrowStack = Put(alice, 1, arrows, 6);
			Item boltStack = Put(bob, 0, bolts, 9);

			ulong arrowsBefore = Total(alice, arrows) + Total(bob, arrows);
			ulong boltsBefore = Total(alice, bolts) + Total(bob, bolts);
			var idsBefore = new HashSet<long>(Identities(alice));
			idsBefore.UnionWith(Identities(bob));

			TradeExchange.Side a = Side(alice, OfferOf(swordItem), OfferOf(arrowStack));
			TradeExchange.Side b = Side(bob, OfferOf(boltStack));

			LogAssert.IsTrue(TradeExchange.TryApply(a, b, out TradeExchange.Failure failure), $"applies ({failure})");

			LogAssert.AreEqual(arrowsBefore, Total(alice, arrows) + Total(bob, arrows), "arrows conserved");
			LogAssert.AreEqual(boltsBefore, Total(alice, bolts) + Total(bob, bolts), "bolts conserved");
			LogAssert.AreEqual(0UL, Total(alice, arrows), "Alice gave all her arrows");
			LogAssert.AreEqual(9UL, Total(alice, bolts), "Alice got all the bolts");
			LogAssert.AreEqual(6UL, Total(bob, arrows), "Bob got all the arrows");
			LogAssert.IsTrue(bob.ContainsItem(sword) && !alice.ContainsItem(sword), "the sword crossed");

			var idsAfter = new HashSet<long>(Identities(alice));
			idsAfter.UnionWith(Identities(bob));
			LogAssert.IsTrue(idsBefore.SetEquals(idsAfter), "no identity minted or lost");
		}

		[Test]
		public void AWholeStack_ThatMergesEntirelyIntoTheReceiversStack_IsListedRemovedByTheGiver()
		{
			Item aliceArrows = Put(alice, 0, arrows, 3);
			Item bobArrows = Put(bob, 4, arrows, 5);
			long givenID = aliceArrows.ID;

			TradeExchange.Side a = Side(alice, OfferOf(aliceArrows));
			TradeExchange.Side b = Side(bob);

			LogAssert.IsTrue(TradeExchange.TryApply(a, b, out TradeExchange.Failure failure), $"applies ({failure})");

			LogAssert.AreEqual(8u, bobArrows.Stackable.Amount, "Bob's stack absorbed the whole gift");
			LogAssert.IsFalse(Identities(bob).Contains(givenID), "the given instance has no slot in Bob's bag");
			LogAssert.AreEqual(1, a.Removed.Count, "Alice's write deletes the row that ceased to exist");
			LogAssert.AreEqual(givenID, a.Removed[0].ItemID, "by its identity");
			LogAssert.AreEqual(0, a.Removed[0].Slot, "recording the slot it left");
			LogAssert.AreEqual(1, b.Changed.Count, "Bob's write carries the grown stack");
			LogAssert.AreSame(bobArrows, b.Changed[0], "the stack that absorbed it");
			LogAssert.AreEqual(0, b.UnidentifiedPlaced.Count, "nothing new was placed");
		}

		// ── Partial offers ──────────────────────────────────────────────────────────────────

		[Test]
		public void APartialOffer_SplitsTheStack_AndTheReceiverGetsAnUnidentifiedItem()
		{
			Item aliceArrows = Put(alice, 0, arrows, 9);
			long sourceID = aliceArrows.ID;

			TradeExchange.Side a = Side(alice, OfferOf(aliceArrows, 4));
			TradeExchange.Side b = Side(bob);

			LogAssert.IsTrue(TradeExchange.TryApply(a, b, out TradeExchange.Failure failure), $"applies ({failure})");

			LogAssert.AreEqual(5u, aliceArrows.Stackable.Amount, "Alice keeps 9 - 4");
			LogAssert.AreEqual(sourceID, aliceArrows.ID, "under her original identity");
			LogAssert.AreSame(aliceArrows, At(alice, 0), "in the same slot");
			LogAssert.AreEqual(4UL, Total(bob, arrows), "Bob got 4");

			LogAssert.AreEqual(1, a.Changed.Count, "Alice's write carries the reduced stack");
			LogAssert.AreSame(aliceArrows, a.Changed[0], "her stack");
			LogAssert.AreEqual(0, a.Removed.Count, "nothing deleted");
			LogAssert.AreEqual(0, a.EmptiedSlots.Count, "her slot is still occupied");

			LogAssert.AreEqual(1, b.Changed.Count, "Bob's write carries the split half");
			LogAssert.AreEqual(0L, b.Changed[0].ID, "which has no identity yet");
			LogAssert.AreEqual(1, b.UnidentifiedPlaced.Count, "and is reported so its slot can be locked until the database names it");
			LogAssert.AreSame(b.Changed[0], b.UnidentifiedPlaced[0], "the same instance");
		}

		[Test]
		public void APartialOffer_ThatMergesEntirely_ListsNothingToDelete()
		{
			Item aliceArrows = Put(alice, 0, arrows, 9);
			Item bobArrows = Put(bob, 0, arrows, 2);

			TradeExchange.Side a = Side(alice, OfferOf(aliceArrows, 4));
			TradeExchange.Side b = Side(bob);

			LogAssert.IsTrue(TradeExchange.TryApply(a, b, out TradeExchange.Failure failure), $"applies ({failure})");

			LogAssert.AreEqual(5u, aliceArrows.Stackable.Amount, "Alice keeps 5");
			LogAssert.AreEqual(6u, bobArrows.Stackable.Amount, "Bob's stack grew by 4");
			LogAssert.AreEqual(0, a.Removed.Count, "a split half never had a row: nothing to delete");
			LogAssert.AreEqual(1, b.Changed.Count, "Bob's write carries his grown stack");
			LogAssert.AreSame(bobArrows, b.Changed[0], "that stack");
			LogAssert.AreEqual(0, b.UnidentifiedPlaced.Count, "the split half was absorbed, not placed");
		}

		// ── Refusals leave both bags untouched ──────────────────────────────────────────────

		[Test]
		public void NoRoomOnOneSide_RefusesTheWholeExchange_AndRestoresBothBags()
		{
			Item swordItem = Put(alice, 0, sword, 1);
			Item aliceArrows = Put(alice, 1, arrows, 7);
			Item bobBolts = Put(bob, 0, bolts, 3);
			Fill(bob, bolts);

			// Bob is full. Alice gives two things; Bob gives one. Alice has room, Bob has none
			// (his own leaving slot could take ONE of hers, not two).
			ulong aliceArrowsBefore = Total(alice, arrows);
			ulong bobBoltsBefore = Total(bob, bolts);
			var aliceIDs = Identities(alice);
			var bobIDs = Identities(bob);

			TradeExchange.Side a = Side(alice, OfferOf(swordItem), OfferOf(aliceArrows));
			TradeExchange.Side b = Side(bob, OfferOf(bobBolts));

			LogAssert.IsFalse(TradeExchange.TryApply(a, b, out TradeExchange.Failure failure), "refused");
			LogAssert.AreEqual(TradeExchange.Failure.NoRoom, failure, "for room");

			LogAssert.AreSame(swordItem, At(alice, 0), "Alice's sword is back in slot 0");
			LogAssert.AreSame(aliceArrows, At(alice, 1), "Alice's arrows are back in slot 1");
			LogAssert.AreEqual(7u, aliceArrows.Stackable.Amount, "with their full amount");
			LogAssert.AreSame(bobBolts, At(bob, 0), "Bob's bolts are back in slot 0");
			LogAssert.AreEqual(3u, bobBolts.Stackable.Amount, "with their full amount");
			LogAssert.AreEqual(aliceArrowsBefore, Total(alice, arrows), "Alice's arrow total unchanged");
			LogAssert.AreEqual(bobBoltsBefore, Total(bob, bolts), "Bob's bolt total unchanged");
			LogAssert.IsTrue(aliceIDs.SetEquals(Identities(alice)), "Alice's identities unchanged");
			LogAssert.IsTrue(bobIDs.SetEquals(Identities(bob)), "Bob's identities unchanged");

			LogAssert.AreEqual(0, a.Changed.Count + a.Removed.Count + a.EmptiedSlots.Count, "Alice's write set is empty");
			LogAssert.AreEqual(0, b.Changed.Count + b.Removed.Count + b.EmptiedSlots.Count, "Bob's write set is empty");
		}

		[Test]
		public void NoRoom_AfterAPartialMerge_RestoresTheMergedStackToo()
		{
			// Bob is full and holds a nearly-full arrow stack. Alice's arrows merge partly into
			// it and the remainder finds no slot: the merge must be undone as well.
			Item aliceArrows = Put(alice, 0, arrows, 8);
			Item bobArrows = Put(bob, 0, arrows, 7);
			Fill(bob, bolts);

			TradeExchange.Side a = Side(alice, OfferOf(aliceArrows));
			TradeExchange.Side b = Side(bob);

			LogAssert.IsFalse(TradeExchange.TryApply(a, b, out TradeExchange.Failure failure), "refused");
			LogAssert.AreEqual(TradeExchange.Failure.NoRoom, failure, "for room");
			LogAssert.AreEqual(7u, bobArrows.Stackable.Amount, "Bob's stack is back to 7");
			LogAssert.AreEqual(8u, aliceArrows.Stackable.Amount, "Alice's stack is back to 8");
			LogAssert.AreSame(aliceArrows, At(alice, 0), "in her slot");
		}

		[Test]
		public void APartialOffer_RefusedForRoom_PutsTheQuantityBackOnTheSource()
		{
			Item aliceArrows = Put(alice, 0, arrows, 9);
			Fill(bob, bolts);

			TradeExchange.Side a = Side(alice, OfferOf(aliceArrows, 4));
			TradeExchange.Side b = Side(bob);

			LogAssert.IsFalse(TradeExchange.TryApply(a, b, out _), "refused");
			LogAssert.AreEqual(9u, aliceArrows.Stackable.Amount, "the 4 went back on the stack");
			LogAssert.AreEqual(9UL, Total(alice, arrows), "and nowhere else");
		}

		[Test]
		public void AnOfferForAnItemThatChanged_IsRefusedBeforeAnythingMoves()
		{
			Item aliceArrows = Put(alice, 0, arrows, 9);
			Item bobBolts = Put(bob, 0, bolts, 3);

			TradeOffer stale = new TradeOffer(0, aliceArrows.ID + 999, arrows.ID, 0, 9);

			TradeExchange.Side a = Side(alice, stale);
			TradeExchange.Side b = Side(bob, OfferOf(bobBolts));

			LogAssert.IsFalse(TradeExchange.TryApply(a, b, out TradeExchange.Failure failure), "refused");
			LogAssert.AreEqual(TradeExchange.Failure.OfferInvalid, failure, "as invalid");
			LogAssert.AreSame(aliceArrows, At(alice, 0), "Alice untouched");
			LogAssert.AreSame(bobBolts, At(bob, 0), "Bob untouched: his half was never taken");
		}

		[Test]
		public void AnOfferForMoreThanTheStackHolds_IsRefused()
		{
			Item aliceArrows = Put(alice, 0, arrows, 3);
			TradeOffer tooMany = new TradeOffer(0, aliceArrows.ID, arrows.ID, 0, 5);

			LogAssert.IsFalse(TradeExchange.TryApply(Side(alice, tooMany), Side(bob), out TradeExchange.Failure failure), "refused");
			LogAssert.AreEqual(TradeExchange.Failure.OfferInvalid, failure, "as invalid");
			LogAssert.AreEqual(3u, aliceArrows.Stackable.Amount, "untouched");
		}

		[Test]
		public void ALockedOfferedSlot_IsRefused()
		{
			// The commit unlocks before applying; a slot still locked means something else
			// holds it (a consumable mid-activation) and the exchange must not take it.
			Item aliceArrows = Put(alice, 0, arrows, 3);
			alice.LockSlot(0);

			LogAssert.IsFalse(TradeExchange.TryApply(Side(alice, OfferOf(aliceArrows)), Side(bob), out TradeExchange.Failure failure), "refused");
			LogAssert.AreEqual(TradeExchange.Failure.OfferInvalid, failure, "as invalid");
			LogAssert.AreSame(aliceArrows, At(alice, 0), "untouched");
		}

		[Test]
		public void TwoEmptyTables_ApplyAndChangeNothing()
		{
			Item swordItem = Put(alice, 0, sword, 1);

			TradeExchange.Side a = Side(alice);
			TradeExchange.Side b = Side(bob);

			LogAssert.IsTrue(TradeExchange.TryApply(a, b, out _), "nothing to do is a success");
			LogAssert.AreSame(swordItem, At(alice, 0), "untouched");
			LogAssert.AreEqual(0, a.Changed.Count + b.Changed.Count, "nothing to write");
		}

		// ── Undo: the commit-refused path ───────────────────────────────────────────────────

		[Test]
		public void Undo_AfterAMixedExchange_RestoresBothBagsExactly()
		{
			Item swordItem = Put(alice, 0, sword, 1);
			Item aliceArrows = Put(alice, 1, arrows, 9);
			Item bobArrows = Put(bob, 2, arrows, 4);
			Item bobBolts = Put(bob, 5, bolts, 6);

			TradeExchange.Side a = Side(alice, OfferOf(swordItem), OfferOf(aliceArrows, 3));
			TradeExchange.Side b = Side(bob, OfferOf(bobBolts));

			LogAssert.IsTrue(TradeExchange.TryApply(a, b, out _, out TradeExchange.Applied applied), "applies");
			LogAssert.IsNotNull(applied, "a handle comes back");
			LogAssert.AreEqual(7u, bobArrows.Stackable.Amount, "precondition: Alice's 3 merged into Bob's 4");
			LogAssert.IsTrue(bob.ContainsItem(sword), "precondition: the sword crossed");

			applied.Undo();

			LogAssert.IsTrue(applied.IsUndone, "undone");
			LogAssert.AreSame(swordItem, At(alice, 0), "the sword is back in Alice's slot 0");
			LogAssert.AreSame(aliceArrows, At(alice, 1), "Alice's arrows are back in slot 1");
			LogAssert.AreEqual(9u, aliceArrows.Stackable.Amount, "with all 9");
			LogAssert.AreSame(bobArrows, At(bob, 2), "Bob's arrows are in slot 2");
			LogAssert.AreEqual(4u, bobArrows.Stackable.Amount, "back at 4");
			LogAssert.AreSame(bobBolts, At(bob, 5), "Bob's bolts are back in slot 5");
			LogAssert.AreEqual(6u, bobBolts.Stackable.Amount, "with all 6");
			LogAssert.IsFalse(bob.ContainsItem(sword), "Bob no longer holds the sword");
			LogAssert.IsFalse(alice.ContainsItem(bolts), "Alice no longer holds the bolts");
			LogAssert.AreEqual(0, a.Changed.Count + a.Removed.Count + b.Changed.Count + b.Removed.Count, "the write sets are cleared");

			applied.Undo();
			LogAssert.AreEqual(9u, aliceArrows.Stackable.Amount, "a second undo changes nothing");
		}

		[Test]
		public void TouchedSlots_NameEverySlotTheUndoNeeds()
		{
			Item swordItem = Put(alice, 0, sword, 1);
			Item aliceArrows = Put(alice, 1, arrows, 9);
			Item bobArrows = Put(bob, 2, arrows, 4);
			Item bobBolts = Put(bob, 5, bolts, 6);

			TradeExchange.Side a = Side(alice, OfferOf(swordItem), OfferOf(aliceArrows, 3));
			TradeExchange.Side b = Side(bob, OfferOf(bobBolts));

			LogAssert.IsTrue(TradeExchange.TryApply(a, b, out _, out _), "applies");

			LogAssert.IsTrue(a.TouchedSlots.Contains(0) && a.TouchedSlots.Contains(1), "Alice: both offered slots");
			LogAssert.IsTrue(a.TouchedSlots.Contains(bobBolts.Slot), "Alice: the slot Bob's bolts landed in");
			LogAssert.IsTrue(b.TouchedSlots.Contains(5), "Bob: his offered slot");
			LogAssert.IsTrue(b.TouchedSlots.Contains(2), "Bob: the stack Alice's arrows merged into");
			LogAssert.IsTrue(b.TouchedSlots.Contains(swordItem.Slot), "Bob: the slot the sword landed in");
		}

		// ── The accept-time room estimate ───────────────────────────────────────────────────

		private static TradeOfferEntry EntryFor(Item item, uint amount = 0)
		{
			uint held = item.IsStackable ? item.Stackable.Amount : 1u;
			return new TradeOfferEntry { Slot = item.Slot, ItemID = item.ID, TemplateID = item.Template.ID, Seed = 0, Amount = amount == 0 ? held : amount };
		}

		private static TradeOfferEntry Incoming(BaseItemTemplate template, uint amount, int slot = 0)
		{
			return new TradeOfferEntry { Slot = slot, ItemID = 9000 + slot, TemplateID = template.ID, Seed = 0, Amount = amount };
		}

		[Test]
		public void HasRoomFor_CountsEmptyUnlockedSlots()
		{
			Fill(bob, bolts);
			bob.RemoveItem(3);
			bob.RemoveItem(5);
			bob.LockSlot(5);

			bool fits = TradeRules.HasRoomFor(bob, new[] { Incoming(sword, 1) }, null, out int required, out int available);

			LogAssert.AreEqual(1, required, "one non-stackable item needs one slot");
			LogAssert.AreEqual(1, available, "one empty unlocked slot; the locked empty one does not count");
			LogAssert.IsTrue(fits, "fits");

			LogAssert.IsFalse(TradeRules.HasRoomFor(bob, new[] { Incoming(sword, 1), Incoming(sword, 1, 1) }, null, out required, out _), "two items into one slot do not fit");
			LogAssert.AreEqual(2, required, "two slots needed");
		}

		[Test]
		public void HasRoomFor_MergesIntoResidentStacks_AndCountsTheOverflow()
		{
			Item bobArrows = Put(bob, 0, arrows, 7); // 3 spare
			Fill(bob, bolts);
			bob.RemoveItem(9); // exactly one empty slot

			LogAssert.IsTrue(TradeRules.HasRoomFor(bob, new[] { Incoming(arrows, 3) }, null, out int required, out int available), "3 arrows merge entirely");
			LogAssert.AreEqual(0, required, "no slot needed");

			LogAssert.IsTrue(TradeRules.HasRoomFor(bob, new[] { Incoming(arrows, 13) }, null, out required, out available), "13 arrows: 3 merge, 10 fill one new stack");
			LogAssert.AreEqual(1, required, "one slot needed");

			LogAssert.IsFalse(TradeRules.HasRoomFor(bob, new[] { Incoming(arrows, 14) }, null, out required, out available), "14 arrows: 3 merge, 11 need two stacks");
			LogAssert.AreEqual(2, required, "two slots needed");
			LogAssert.AreEqual(1, available, "one free");
			LogAssert.AreEqual(7u, bobArrows.Stackable.Amount, "an estimate mutates nothing");
		}

		[Test]
		public void HasRoomFor_CountsSlotsVacatedByOwnWholeOffers_ButNotPartialOnes()
		{
			Item bobSword = Put(bob, 0, sword, 1);
			Item bobBolts = Put(bob, 1, bolts, 6);
			Fill(bob, arrows);

			// Nothing free. Giving the sword away whole frees its slot; giving 2 of 6 bolts does not.
			LogAssert.IsFalse(TradeRules.HasRoomFor(bob, new[] { Incoming(sword, 1) }, null, out _, out int available), "full");
			LogAssert.AreEqual(0, available, "nothing free");

			LogAssert.IsTrue(TradeRules.HasRoomFor(bob, new[] { Incoming(sword, 1) }, new[] { EntryFor(bobSword) }, out _, out available), "the leaving sword frees a slot");
			LogAssert.AreEqual(1, available, "one vacated");

			LogAssert.IsFalse(TradeRules.HasRoomFor(bob, new[] { Incoming(sword, 1) }, new[] { EntryFor(bobBolts, 2) }, out _, out available), "a partial offer keeps its slot");
			LogAssert.AreEqual(0, available, "nothing vacated");
		}

		[Test]
		public void HasRoomFor_DoesNotMergeIntoAStackThatIsLeaving()
		{
			Item bobArrows = Put(bob, 0, arrows, 2); // 8 spare, but it is on Bob's table
			Fill(bob, bolts);

			bool fits = TradeRules.HasRoomFor(bob, new[] { Incoming(arrows, 5) }, new[] { EntryFor(bobArrows) }, out int required, out int available);

			LogAssert.IsTrue(fits, "the vacated slot takes the incoming stack instead");
			LogAssert.AreEqual(1, required, "one new stack: the leaving stack is not a merge target");
			LogAssert.AreEqual(1, available, "the vacated slot");
		}

		[Test]
		public void AReceiversOwnLeavingSlot_CanHostTheIncomingItem()
		{
			// Bob is full except for what he is giving away; Alice's item must be able to land
			// in the slot Bob's gift vacates. That is why every take precedes every give.
			Item swordItem = Put(alice, 0, sword, 1);
			Item bobBolts = Put(bob, 0, bolts, 3);
			Fill(bob, arrows);

			TradeExchange.Side a = Side(alice, OfferOf(swordItem));
			TradeExchange.Side b = Side(bob, OfferOf(bobBolts));

			LogAssert.IsTrue(TradeExchange.TryApply(a, b, out TradeExchange.Failure failure), $"applies ({failure})");
			LogAssert.IsTrue(bob.ContainsItem(sword), "Bob got the sword");
			LogAssert.IsTrue(alice.ContainsItem(bolts), "Alice got the bolts");
			LogAssert.IsFalse(a.EmptiedSlots.Count == 0, "Alice's slot 0 emptied and is reported");
			LogAssert.IsTrue(bob.FreeSlots() == 0, "Bob is exactly as full as before");
		}
	}
}
