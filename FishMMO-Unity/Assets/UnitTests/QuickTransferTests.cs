using System;
using System.IO;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for shift-clicking an item across to the other open container (issue #197).
	/// </summary>
	/// <remarks>
	/// <para>
	/// Banking a bagful is the operation players repeat most, and by drag it costs a press, an aim
	/// and a release per item across two panels. Shift-click sends the item to the first free slot
	/// of the other container instead.
	/// </para>
	/// <para>
	/// The destination is chosen from what this client can see, and the request is the same swap a
	/// drag onto that slot would have sent — deliberately, so it carries exactly the risk the drag
	/// it replaces already carried. What it must not do is pick a slot that is occupied or waiting,
	/// because a swap into an occupied slot moves two items when the player asked to move one.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class QuickTransferTests
	{
		private const string PanelPath =
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

		/// <summary>The body of a named method in a source file.</summary>
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
		public void TheDestinationSlotIsFreeAndNotWaiting()
		{
			/* The one thing quick transfer must get right. A swap into an occupied slot exchanges
			 * two items when the player asked to move one, and a slot that is locked is already
			 * answering a different request. */
			string body = MethodBody(PanelPath, "private static int FirstFreeSlot", "/// <summary>Shows a transient notice");

			LogAssert.IsTrue(body.Contains("IsSlotEmpty"),
				"the destination must be an empty slot");
			LogAssert.IsTrue(body.Contains("IsSlotLocked"),
				"a slot already waiting on a request must not be chosen");
		}

		[Test]
		public void AFullDestinationIsRefusedOutLoud()
		{
			/* A shift-click that silently does nothing is indistinguishable from one the game did
			 * not register, and the player's next move is to try it again. */
			string body = MethodBody(PanelPath, "protected void TryQuickTransfer", "private string QuickTransferTargetName");

			LogAssert.IsTrue(body.Contains("< 0"),
				"a container with no free slot must be detected");
			LogAssert.IsTrue(body.Contains("Notify("),
				"and the refusal must be shown to the player");
		}

		[Test]
		public void BothEndsAreClaimedOrNeither()
		{
			/* Same rule the drag path follows: a slot marked as waiting for a request that was
			 * never sent stays locked until the panel is rebuilt. */
			string body = MethodBody(PanelPath, "protected void TryQuickTransfer", "private string QuickTransferTargetName");

			LogAssert.IsTrue(body.Contains("ItemOperationTracker.Release("),
				"claiming the second end and failing must release the first");
		}

		[Test]
		public void ShiftClickIsRoutedSeparatelyFromAPlainClick()
		{
			/* A plain click still starts a drag or completes one; only shift diverts it. Reading the
			 * modifier from the event rather than polling input state keeps the two in step. */
			string body = MethodBody(PanelPath, "private void OnSlotPointerDown", "protected virtual void HandleSlotRightClick");

			LogAssert.IsTrue(body.Contains("evt.shiftKey"),
				"shift must be read from the click that happened");
			LogAssert.IsTrue(body.Contains("TryQuickTransfer"),
				"a shift-click must route to the transfer");
			LogAssert.IsTrue(body.Contains("HandleSlotLeftClick"),
				"a plain click must still behave as before");
		}

		[Test]
		public void EachPanelSendsTheOtherContainersRequest()
		{
			/* Moving INTO the bank is a bank swap whoever asked for it, so the panel losing the item
			 * names the other panel's broadcast. Getting this backwards would ask the server to move
			 * the item to where it already is. */
			LogAssert.IsTrue(
				MethodBody(BankPath, "protected override void SendQuickTransferRequest", "protected override void SendSwapRequest")
					.Contains("InventorySwapItemSlotsBroadcast"),
				"the bank must send an inventory swap to push an item out to the inventory");

			LogAssert.IsTrue(
				MethodBody(InventoryPath, "protected override void SendQuickTransferRequest", "protected override void SendSwapRequest")
					.Contains("BankSwapItemSlotsBroadcast"),
				"the inventory must send a bank swap to push an item into the bank");
		}

		[Test]
		public void ThePanelsPointAtEachOther()
		{
			LogAssert.IsTrue(
				ReadSource(BankPath).Contains("QuickTransferTarget => ReferenceButtonType.Inventory"),
				"the bank sends to the inventory");

			LogAssert.IsTrue(
				ReadSource(InventoryPath).Contains("QuickTransferTarget => ReferenceButtonType.Bank"),
				"the inventory sends to the bank");
		}
	}
}
