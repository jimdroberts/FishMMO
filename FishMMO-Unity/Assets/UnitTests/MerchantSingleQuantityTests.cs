using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Buying something that does not stack hands over exactly one of it, and only when the buyer
	/// can pay for it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>MaxStackSize</c> is 1 for anything non-stacking, and 0 where nobody set it — both sides
	/// read 0 as 1. There is no quantity to choose for such an entry, and the panel does not offer
	/// one (see <see cref="MerchantPanelTests"/>). These hold the other half: that the offer is
	/// not merely hidden but impossible, so a client that asks for twenty anyway still gets one.
	/// </para>
	/// <para>
	/// The grant is proved against a real <c>InventoryController</c> rather than read off the
	/// purchase handler, because the guarantee does not come from the handler's clamp alone —
	/// <c>Item</c> refuses to build a stackable component at all below a max of two, so the
	/// quantity has nowhere to live even if the clamp were bypassed.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class MerchantSingleQuantityTests
	{
		private const string ServerPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/Interactable/InteractableSystem.Merchant.cs";

		private readonly List<GameObject> gameObjects = new List<GameObject>();
		private readonly List<UnityEngine.Object> assets = new List<UnityEngine.Object>();
		private InventoryController inventory;

		[SetUp]
		public void SetUp()
		{
			GameObject go = new GameObject("MerchantSingleQuantityProbe");
			gameObjects.Add(go);
			inventory = go.AddComponent<InventoryController>();
			inventory.OnAwake();
			inventory.InitializeOnce(new FishMMO.UnitTests.Harness.StubCharacter { ID = 11 });
		}

		[TearDown]
		public void TearDown()
		{
			foreach (UnityEngine.Object asset in assets) UnityEngine.Object.DestroyImmediate(asset);
			foreach (GameObject go in gameObjects) UnityEngine.Object.DestroyImmediate(go);
			assets.Clear();
			gameObjects.Clear();
		}

		private BaseItemTemplate Template(string name, uint maxStackSize, int price)
		{
			ProofItemTemplate t = ScriptableObject.CreateInstance<ProofItemTemplate>();
			t.name = name;
			t.MaxStackSize = maxStackSize;
			t.Price = price;
			t.AddToCache(t.name);
			assets.Add(t);
			return t;
		}

		/// <summary>The server's clamp, applied to a request the way TryPurchaseItem applies it.</summary>
		private static long ClampAsTheServerDoes(BaseItemTemplate template, long requestedQuantity, long balance)
		{
			long unitPrice = template.Price;
			long maxStack = template.MaxStackSize > 0 ? template.MaxStackSize : 1;
			long requested = requestedQuantity <= 0 ? 1 : requestedQuantity;
			long quantity = Math.Min(requested, maxStack);
			if (unitPrice > 0)
			{
				quantity = Math.Min(quantity, balance / unitPrice);
			}
			return quantity;
		}

		[Test]
		public void AMaxStackOfOne_ClampsAnyRequestToOne()
		{
			BaseItemTemplate sword = Template("SingleQty_Sword", 1, 25);

			LogAssert.AreEqual(1L, ClampAsTheServerDoes(sword, 20, 1000), "twenty asked for, one allowed");
			LogAssert.AreEqual(1L, ClampAsTheServerDoes(sword, int.MaxValue, long.MaxValue / 2), "and an absurd request too");
			LogAssert.AreEqual(1L, ClampAsTheServerDoes(sword, 0, 1000), "a missing quantity means one, not none");
		}

		[Test]
		public void AMaxStackOfZero_IsReadAsOne()
		{
			/* MaxStackSize is a uint with no authoring guard, so 0 is reachable and means the same
			 * thing 1 does. Both sides agree on that: the panel's ceiling and the server's clamp
			 * apply the identical `> 0 ? value : 1` rule. */
			BaseItemTemplate unset = Template("SingleQty_Unset", 0, 25);

			LogAssert.AreEqual(1L, ClampAsTheServerDoes(unset, 20, 1000), "an unset stack size is one, not zero");
		}

		[Test]
		public void ABuyerWhoCannotAffordOne_GetsNothing()
		{
			/* The clamp collapses to zero, and the handler refuses at `quantity < 1` with
			 * InsufficientFunds rather than granting a free sword. */
			BaseItemTemplate sword = Template("SingleQty_Costly", 1, 25);

			LogAssert.AreEqual(0L, ClampAsTheServerDoes(sword, 1, 24), "one short is still short");
			LogAssert.AreEqual(0L, ClampAsTheServerDoes(sword, 1, 0), "and an empty purse buys nothing");
			LogAssert.AreEqual(1L, ClampAsTheServerDoes(sword, 1, 25), "exactly the price is enough");
		}

		[Test]
		public void AFreeSingleItem_IsStillOnlyOne()
		{
			BaseItemTemplate free = Template("SingleQty_Free", 1, 0);

			LogAssert.AreEqual(1L, ClampAsTheServerDoes(free, 20, 0),
				"free does not mean unlimited, and a zero balance is irrelevant to a zero price");
		}

		[Test]
		public void TheHandlerRefusesBelowOneAndNeverGrantsMoreThanOneStack()
		{
			/* The two lines the arithmetic above stands in for. If either moves, the model here
			 * stops describing the server and these tests stop meaning anything. */
			string source = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), ServerPath)).Replace("\r\n", "\n");

			LogAssert.IsTrue(source.Contains("long maxStack = itemTemplate.MaxStackSize > 0 ? itemTemplate.MaxStackSize : 1;"),
				"the ceiling must still be one stack, with 0 read as 1");
			LogAssert.IsTrue(source.Contains("long quantity = Math.Min(requested, maxStack);"),
				"the request must still be clamped to that ceiling");
			LogAssert.IsTrue(source.Contains("long affordable = balance / unitPrice;"),
				"and then to what the buyer can pay for");
			LogAssert.IsTrue(source.Contains("if (quantity < 1)"),
				"a buyer who cannot afford one must be refused, not given one");
		}

		[Test]
		public void GrantingASingleItem_PutsOneNonStackingInstanceInOneSlot()
		{
			BaseItemTemplate sword = Template("SingleQty_Granted", 1, 25);

			// Exactly what TryPurchaseItem builds once its clamp has run.
			Item granted = new Item(sword, 1);

			LogAssert.IsFalse(granted.IsStackable, "a max stack of one is not a stack");
			LogAssert.IsTrue(inventory.TryAddItem(granted, out List<Item> modified), "the grant lands");
			LogAssert.AreEqual(1, modified.Count, "one slot changed");
			LogAssert.AreEqual(1, inventory.FilledSlots(), "and holds one instance");
		}

		[Test]
		public void AnOversizedAmountCannotBecomeAStackOnASingleItem()
		{
			/* Belt and braces for the clamp: even handed twenty directly, Item builds no stackable
			 * component below a max of two, so the amount has nowhere to live. */
			BaseItemTemplate sword = Template("SingleQty_Oversized", 1, 25);

			Item granted = new Item(sword, 20);

			LogAssert.IsFalse(granted.IsStackable, "still not a stack");
			LogAssert.IsTrue(inventory.TryAddItem(granted, out List<Item> modified), "it lands as one item");
			LogAssert.AreEqual(1, modified.Count, "in one slot");
			LogAssert.AreEqual(1, inventory.FilledSlots(), "and occupies exactly one");
		}

		[Test]
		public void AStackableItemStillStacks()
		{
			/* The control: none of the above is a blanket ban on quantity. */
			BaseItemTemplate bread = Template("SingleQty_Bread", 20, 4);

			LogAssert.AreEqual(5L, ClampAsTheServerDoes(bread, 5, 1000), "five is five when the stack allows it");
			LogAssert.AreEqual(20L, ClampAsTheServerDoes(bread, 50, 1000), "and fifty is trimmed to one stack");
			LogAssert.AreEqual(3L, ClampAsTheServerDoes(bread, 20, 12), "and again to what the purse allows");

			Item granted = new Item(bread, 5);
			LogAssert.IsTrue(granted.IsStackable, "bread stacks");
			LogAssert.AreEqual(5u, granted.Stackable.Amount, "with the quantity that was bought");
		}

		private sealed class ProofItemTemplate : BaseItemTemplate { }
	}
}
