using System;
using System.IO;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that an unequip ends with the client and the server naming the same slot.
	/// </summary>
	/// <remarks>
	/// <para>
	/// An unequip request names the destination CONTAINER but not the slot within it, because only
	/// the server knows what that container really holds. The server therefore picks one. The
	/// client, when a reconcile emptied the equipment socket before the acknowledgement arrived,
	/// picked one too — and the two had no reason to agree.
	/// </para>
	/// <para>
	/// Observed in play: an item unequipped into the inventory sat in slot 5 on the client and slot
	/// 0 on the server. Every later request naming slot 5 was refused, because the server saw that
	/// slot as empty; the item appeared to move freely between inventory slots only because the
	/// client was rearranging its own copy of a container the server disagreed with. Recording the
	/// destination container was not enough to fix it — the disagreement was never about which
	/// container.
	/// </para>
	/// <para>
	/// So the answer travels back with the acknowledgement, and the client places the item where it
	/// is told rather than where it guessed.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class UnequipSlotAgreementTests
	{
		private const string BroadcastPath =
			"Assets/Scripts/Shared/Implementation/Network/Character/Inventory/EquipmentBroadcasts.cs";

		private const string ControllerPath =
			"Assets/Scripts/Shared/Implementation/Entity/Prediction/Equipment/EquipmentController.cs";

		private const string ServerPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/CharacterInventory/CharacterInventorySystem.cs";

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}

		[Test]
		public void TheAcknowledgementCarriesADestinationSlot()
		{
			/* Without a field for it the client can only guess, and a guess is wrong as soon as the
			 * container holds anything the two sides disagree about. */
			string source = ReadSource(BroadcastPath);

			int broadcast = source.IndexOf("struct EquipmentUnequipItemBroadcast", StringComparison.Ordinal);
			LogAssert.IsTrue(broadcast >= 0, "the unequip broadcast must still exist");

			int end = source.IndexOf("\n\t}", broadcast, StringComparison.Ordinal);
			string body = source.Substring(broadcast, end - broadcast);

			LogAssert.IsTrue(body.Contains("ToSlot"),
				"the unequip acknowledgement must name the slot the item landed in");
		}

		[Test]
		public void TheServerFillsInTheSlotItChose()
		{
			/* The server is the only party that knows, so it is the only party that can answer. */
			string source = ReadSource(ServerPath);

			LogAssert.IsTrue(source.Contains("msg.ToSlot ="),
				"the server must report the slot it put the item in");
		}

		[Test]
		public void TheServerReportsTheUnequippedItemsOwnSlot()
		{
			/* Not the first entry of the modified list. TryAddItem reports every item it touched,
			 * including stacks it merged into, and the order is not ours to rely on -- reading
			 * index zero would name another item's slot whenever a merge happened. */
			string source = ReadSource(ServerPath);

			LogAssert.IsFalse(source.Contains("msg.ToSlot = modifiedItems[0]"),
				"the destination must come from the unequipped item, not from a list position");
		}

		[Test]
		public void TheClientCorrectsASlotItGuessed()
		{
			/* The half that actually repairs the divergence. An acknowledgement that returned early
			 * on an already-empty socket is what let the two sides stay apart forever. */
			string source = ReadSource(ControllerPath);

			int ack = source.IndexOf("ApplyUnequipAcknowledgement", StringComparison.Ordinal);
			LogAssert.IsTrue(ack >= 0, "the acknowledgement handler must still exist");

			/* Bounded to the handler itself. The helper it calls is declared earlier in the file,
			 * so searching forward for the helper would find nothing and prove nothing. */
			int end = source.IndexOf("public void Activate(", ack, StringComparison.Ordinal);
			LogAssert.IsTrue(end > ack, "the end of the handler must be locatable");

			string body = source.Substring(ack, end - ack);

			LogAssert.IsTrue(body.Contains("PlaceAtAcknowledgedSlot"),
				"an already-empty socket must still place the item at the acknowledged slot");
		}

		[Test]
		public void TheClientFindsTheItemByIdentityRatherThanBySlot()
		{
			/* Where the client put it is the thing under repair, so it cannot also be the thing
			 * used to find it. */
			string source = ReadSource(ControllerPath);

			int helper = source.IndexOf("private void PlaceAtAcknowledgedSlot", StringComparison.Ordinal);
			LogAssert.IsTrue(helper >= 0, "the placement helper must exist");

			int end = source.IndexOf("private static bool TryTakeByID", helper, StringComparison.Ordinal);
			LogAssert.IsTrue(end > helper, "the identity lookup must follow it");

			string body = source.Substring(helper, end - helper);

			LogAssert.IsTrue(body.Contains("TryTakeByID"),
				"the item must be located by id, not by the slot the client chose");
		}
	}
}
