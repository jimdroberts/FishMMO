using System;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared;
using AuthTestTrace = FishMMO.UnitTests.Harness.AuthTestTrace;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Quantity-conservation proofs for <see cref="ItemStackable"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// These exist because of CRIT-1: <c>AddToStack</c> used <c>UIntExtensions.AbsoluteSubtract</c>,
	/// which returns |a - b| rather than a saturating subtract, so the ordinary "it all fits" branch
	/// wrote the UNUSED capacity back onto the donor and underflowed the destination. The published
	/// reproduction — MaxStackSize 10, destination 1, source 2 — turned three items into
	/// 4,294,967,299. Every test here therefore asserts the same single property in different
	/// shapes: <c>a.Amount + b.Amount</c> is identical before and after any stack operation.
	/// </para>
	/// <para>
	/// The invariant is checked with <see cref="ulong"/> arithmetic on purpose. Summing two
	/// <see cref="uint"/> stacks in a <see cref="uint"/> would wrap in exactly the failure case these
	/// tests are meant to catch, and the assertion would pass on corrupt data.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class ItemStackConservationTests
	{
		private const uint MaxStack = 10;

		private StackableTestTemplate stackableTemplate;
		private StackableTestTemplate otherTemplate;

		[SetUp]
		public void SetUp()
		{
			stackableTemplate = ScriptableObject.CreateInstance<StackableTestTemplate>();
			stackableTemplate.MaxStackSize = MaxStack;
			stackableTemplate.Generate = false;
			stackableTemplate.name = "TestStackableTemplate";
			stackableTemplate.AddToCache(stackableTemplate.name);

			otherTemplate = ScriptableObject.CreateInstance<StackableTestTemplate>();
			otherTemplate.MaxStackSize = MaxStack;
			otherTemplate.Generate = false;
			otherTemplate.name = "TestStackableTemplateOther";
			otherTemplate.AddToCache(otherTemplate.name);
		}

		[TearDown]
		public void TearDown()
		{
			Resources.UnloadUnusedAssets();
		}

		/// <summary>
		/// A concrete, inert item template. <see cref="BaseItemTemplate"/> is abstract, and the
		/// stack maths never consults anything but MaxStackSize, ID and Generate.
		/// </summary>
		private class StackableTestTemplate : BaseItemTemplate
		{
		}

		private static ulong TotalOf(Item a, Item b)
		{
			ulong total = 0;
			if (a != null && a.IsStackable) total += a.Stackable.Amount;
			if (b != null && b.IsStackable) total += b.Stackable.Amount;
			return total;
		}

		private Item MakeItem(uint amount)
		{
			return new Item(stackableTemplate, amount);
		}

		[Test]
		public void AddToStack_SourceFitsEntirely_ConservesQuantity()
		{
			RunTraced(nameof(AddToStack_SourceFitsEntirely_ConservesQuantity),
				"CRIT-1's exact reproduction: MaxStackSize 10, destination 1, source 2 must yield 3 and 0.",
				() =>
				{
					Item destination = MakeItem(1);
					Item source = MakeItem(2);

					ulong before = TotalOf(destination, source);
					LogAssert.AreEqual(3UL, before, "Precondition: the two stacks must start holding 3 items between them.");

					bool merged = destination.Stackable.AddToStack(source);

					LogAssert.IsTrue(merged, "A 2-item stack must merge into a 1-item stack with a cap of 10.");
					LogAssert.AreEqual(3u, destination.Stackable.Amount, "The destination must hold exactly 1 + 2.");
					LogAssert.AreEqual(0u, source.Stackable.Amount, "The donor must be emptied, not topped up with the unused capacity.");
					LogAssert.AreEqual(before, TotalOf(destination, source), "Total quantity must be unchanged by the merge.");
				});
		}

		[Test]
		public void AddToStack_SourceOverflows_ConservesQuantityAndCaps()
		{
			RunTraced(nameof(AddToStack_SourceOverflows_ConservesQuantityAndCaps),
				"An oversized donor must fill the destination to the cap and keep the remainder itself.",
				() =>
				{
					Item destination = MakeItem(8);
					Item source = MakeItem(7);

					ulong before = TotalOf(destination, source);

					bool merged = destination.Stackable.AddToStack(source);

					LogAssert.IsTrue(merged, "A partial merge must still report success.");
					LogAssert.AreEqual(MaxStack, destination.Stackable.Amount, "The destination must fill to exactly MaxStackSize.");
					LogAssert.AreEqual(5u, source.Stackable.Amount, "The donor must retain 7 - 2 = 5.");
					LogAssert.AreEqual(before, TotalOf(destination, source), "Total quantity must be unchanged by a partial merge.");
				});
		}

		[Test]
		public void AddToStack_IntoFullStack_IsRefusedAndChangesNothing()
		{
			RunTraced(nameof(AddToStack_IntoFullStack_IsRefusedAndChangesNothing),
				"A full destination must refuse the merge outright rather than underflow into a ~4 billion stack.",
				() =>
				{
					Item destination = MakeItem(MaxStack);
					Item source = MakeItem(3);

					ulong before = TotalOf(destination, source);

					bool merged = destination.Stackable.AddToStack(source);

					LogAssert.IsFalse(merged, "A destination already at MaxStackSize must refuse.");
					LogAssert.AreEqual(MaxStack, destination.Stackable.Amount, "The full destination must be untouched.");
					LogAssert.AreEqual(3u, source.Stackable.Amount, "The donor must be untouched.");
					LogAssert.AreEqual(before, TotalOf(destination, source), "A refused merge must conserve quantity.");
				});
		}

		[Test]
		public void AddToStack_SelfMerge_IsRefused()
		{
			RunTraced(nameof(AddToStack_SelfMerge_IsRefused),
				"TryAddItem walks every slot and can hand a stack itself; merging into itself would double it.",
				() =>
				{
					Item item = MakeItem(4);

					bool merged = item.Stackable.AddToStack(item);

					LogAssert.IsFalse(merged, "A stack must refuse to merge with itself.");
					LogAssert.AreEqual(4u, item.Stackable.Amount, "A refused self-merge must not change the amount.");
					LogAssert.IsFalse(item.Stackable.CanAddToStack(item), "CanAddToStack must agree with AddToStack about self-merges.");
				});
		}

		[Test]
		public void TryUnstack_Partial_ConservesQuantityAcrossTheSplit()
		{
			RunTraced(nameof(TryUnstack_Partial_ConservesQuantityAcrossTheSplit),
				"A split must allocate a real new Item and move the quantity, never destroy it.",
				() =>
				{
					Item source = MakeItem(9);
					ulong before = TotalOf(source, null);

					bool split = source.Stackable.TryUnstack(4, out Item taken);

					LogAssert.IsTrue(split, "Splitting 4 off a stack of 9 must succeed.");
					LogAssert.IsNotNull(taken, "A successful split must hand back a real instance.");
					LogAssert.IsFalse(ReferenceEquals(taken, source), "A partial split must be a NEW instance, not the original.");
					LogAssert.AreEqual(5u, source.Stackable.Amount, "The source must be decremented by exactly the split amount.");
					LogAssert.AreEqual(4u, taken.Stackable.Amount, "The split half must carry the requested amount.");
					LogAssert.AreEqual(0L, taken.ID, "A split half is not yet a database row, so its ID must be 0.");
					LogAssert.AreEqual(-1, taken.Slot, "A split half is not yet in a container, so its slot must be -1.");
					LogAssert.AreEqual(before, TotalOf(source, taken), "Total quantity must survive the split.");
				});
		}

		[Test]
		public void TryUnstack_WholeStack_ReturnsTheOriginalUntouched()
		{
			RunTraced(nameof(TryUnstack_WholeStack_ReturnsTheOriginalUntouched),
				"Taking the whole stack is a move, not a split, so the caller gets the original instance back.",
				() =>
				{
					Item source = MakeItem(6);

					bool split = source.Stackable.TryUnstack(6, out Item taken);

					LogAssert.IsTrue(split, "Requesting the whole stack must succeed.");
					LogAssert.IsTrue(ReferenceEquals(taken, source), "Requesting the whole stack must hand back the original instance.");
					LogAssert.AreEqual(6u, source.Stackable.Amount, "The stack must not be decremented when it is moved wholesale.");
				});
		}

		[Test]
		public void TryUnstack_Zero_IsRefusedAndChangesNothing()
		{
			RunTraced(nameof(TryUnstack_Zero_IsRefusedAndChangesNothing),
				"Splitting zero has a defined answer: it is refused, and the stack is untouched.",
				() =>
				{
					Item source = MakeItem(6);

					bool split = source.Stackable.TryUnstack(0, out Item taken);

					LogAssert.IsFalse(split, "Zero is not a split.");
					LogAssert.IsNull(taken, "A refused split hands back nothing.");
					LogAssert.AreEqual(6u, source.Stackable.Amount, "The stack must be untouched.");
				});
		}

		[Test]
		public void TryUnstack_MoreThanHeld_ReturnsTheOriginalUntouched()
		{
			RunTraced(nameof(TryUnstack_MoreThanHeld_ReturnsTheOriginalUntouched),
				"Asking for more than exists is treated as asking for everything: a move of the original, never a decrement past zero.",
				() =>
				{
					Item source = MakeItem(6);

					bool split = source.Stackable.TryUnstack(50, out Item taken);

					LogAssert.IsTrue(split, "Requesting more than the stack holds must not fail silently.");
					LogAssert.IsTrue(ReferenceEquals(taken, source), "It must hand back the original instance.");
					LogAssert.AreEqual(6u, source.Stackable.Amount, "The stack must not be decremented.");
				});
		}

		[Test]
		public void Remove_MoreThanHeld_ClampsToEmptyInsteadOfWrapping()
		{
			RunTraced(nameof(Remove_MoreThanHeld_ClampsToEmptyInsteadOfWrapping),
				"An over-removal must saturate at zero; wrapping would sail straight past the Amount == 0 destroy check.",
				() =>
				{
					Item item = MakeItem(3);

					item.Stackable.Remove(50);

					LogAssert.AreEqual(0u, item.Stackable.Amount, "Removing more than the stack holds must leave it empty, not wrapped.");
				});
		}

		[Test]
		public void Constructor_FromTemplate_InitializesEquippableComponents()
		{
			RunTraced(nameof(Constructor_FromTemplate_InitializesEquippableComponents),
				"Item(template, amount) must run the same Initialize as the id-bearing constructors, "
				+ "otherwise bought and looted gear cannot be equipped until the player relogs.",
				() =>
				{
					var equippableTemplate = ScriptableObject.CreateInstance<EquippableTestTemplate>();
					equippableTemplate.MaxStackSize = 1;
					equippableTemplate.Generate = false;
					equippableTemplate.name = "TestEquippableTemplate";
					equippableTemplate.AddToCache(equippableTemplate.name);

					Item item = new Item(equippableTemplate, 1);

					LogAssert.IsTrue(item.IsEquippable, "An item built from an equippable template must be equippable immediately.");
					LogAssert.AreEqual(0L, item.ID, "The template constructor still assigns the database ID later.");
					LogAssert.AreEqual(-1, item.Slot, "A freshly constructed item is not in a container.");
				});
		}

		/// <summary>
		/// A concrete equippable template. Slot is left at its default; the test only asserts that
		/// the Equippable component was constructed.
		/// </summary>
		private class EquippableTestTemplate : EquippableItemTemplate
		{
		}

		/// <summary>
		/// Wraps a test body in the trace logging the rest of this suite uses.
		/// </summary>
		private static void RunTraced(string testName, string description, Action body)
		{
			try
			{
				AuthTestTrace.LogTestStart(testName, description).GetAwaiter().GetResult();
				body();
				AuthTestTrace.Log("ItemStackConservationTests", "SUCCESS", testName).GetAwaiter().GetResult();
			}
			catch (Exception ex)
			{
				AuthTestTrace.Log("ItemStackConservationTests", "FAILURE", $"{testName}: {ex.Message}\n{ex.StackTrace}").GetAwaiter().GetResult();
				throw;
			}
			finally
			{
				AuthTestTrace.LogTestEnd(testName).GetAwaiter().GetResult();
			}
		}
	}
}
