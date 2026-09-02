using System;
using System.IO;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that the bank's slot counters count items, not icons.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The readout counted non-null entries in the view's cached sprite array. A sprite records
	/// whether an item has an ICON, which is a different question from whether a slot holds an
	/// item, and the two come apart in two ordinary situations: a template with no icon assigned,
	/// and a template whose icon has not finished loading, since
	/// <c>BaseItemTemplate.Icon</c> is an addressable resolved at runtime. Both left a null in the
	/// array, so the bank reported those slots empty while the grid drew the placeholder that the
	/// slot code deliberately provides for exactly that case.
	/// </para>
	/// <para>
	/// Every shipped test item has an empty icon reference, so on the current content the bank
	/// reported zero used no matter how much was in it.
	/// </para>
	/// <para>
	/// The inventory panel had the identical defect and was already fixed by counting from the
	/// controller. The bank was missed, so this pins both panels against the same regression rather
	/// than only the one that was reported.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class BankCapacityReadoutTests
	{
		private const string GridPanelPath =
			"Assets/Scripts/Client/GUI/World/ItemContainers/UITKItemGridPanel.cs";

		private const string BankPath =
			"Assets/Scripts/Client/GUI/World/Bank/UITKBank.cs";

		private const string InventoryPath =
			"Assets/Scripts/Client/GUI/World/Inventory/UITKInventory.cs";

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}

		/// <summary>The body of a panel's RefreshCapacity, where the occupancy decision lives.</summary>
		private static string CapacityBody(string relativePath)
		{
			string source = ReadSource(relativePath);

			int start = source.IndexOf("private void RefreshCapacity()", StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"{relativePath} must still have RefreshCapacity.");

			int end = source.IndexOf("int free = total - used;", start, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"{relativePath}: the counting section must be locatable.");

			return source.Substring(start, end - start);
		}

		[Test]
		public void TheGridPanelCountsItemsRatherThanIcons()
		{
			/* The defect. Counting sprites means an un-iconed item is invisible to the readout,
			 * which on the current content is every item there is.
			 *
			 * Asserted against the shared grid panel now that the bank draws its slots there. That
			 * is the point of moving it: there is one implementation to be right rather than two to
			 * keep in agreement. */
			string body = CapacityBody(GridPanelPath);

			/* The indexed read is the occupancy decision; a mention of the list is not. The
			 * inventory still sizes its total from that list, correctly, so forbidding the name
			 * outright would fail a panel that is already right. */
			LogAssert.IsFalse(body.Contains("slotSprites[i]"),
				"the grid panel must not decide occupancy from cached sprites");

			LogAssert.IsTrue(body.Contains("IsSlotEmpty"),
				"the grid panel must ask the container, which knows what the slot holds");
		}

		[Test]
		public void TheInventoryDrawsItsGridFromTheSharedPanel()
		{
			/* The inventory no longer has a readout of its own to get wrong. That is the whole
			 * point: this bug existed because there were two implementations and a fix reached one
			 * of them, so the durable guard is that there is now one. */
			string source = ReadSource(InventoryPath);

			LogAssert.IsTrue(source.Contains(": UITKItemGridPanel"),
				"the inventory panel must derive its grid from the shared implementation");
		}

		[Test]
		public void TheBankDrawsItsGridFromTheSharedPanel()
		{
			/* The invariant that makes the counting bug unrepeatable. It was fixed in the inventory
			 * and left wrong in the bank because there were two implementations; with one, a fix
			 * cannot land in half the game. */
			string source = ReadSource(BankPath);

			LogAssert.IsTrue(source.Contains(": UITKItemGridPanel"),
				"the bank panel must derive its grid from the shared implementation");
		}

		[Test]
		public void TheGridPanelSizesTheReadoutFromItsSlots()
		{
			/* The total comes from the slots that exist rather than from the sprite cache. Both
			 * lists hold one entry per slot so the two agree today, but once the cache is no longer
			 * the occupancy record it should not be the capacity record either -- and a wrong total
			 * is harder to notice than a wrong used-count, because it looks like a bank of the
			 * wrong size rather than like a miscount.
			 *
			 * Asserted for the bank only. The inventory still takes its total from the sprite cache;
			 * that is correct today and is left alone rather than changed in passing. */
			string body = CapacityBody(GridPanelPath);

			LogAssert.IsTrue(body.Contains("int total = slotViews.Count;"),
				"the total must come from the slots the panel built");
		}
	}
}
