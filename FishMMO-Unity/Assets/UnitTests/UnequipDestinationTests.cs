using System;
using System.IO;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that every equip and unequip request records where the item is meant to go.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The equipment controller keeps a record of a requested move so that a reconcile arriving
	/// before the server's acknowledgement knows which container the item was headed for. Nothing
	/// called it. <c>NotifyEquipRequested</c>, <c>NotifyUnequipRequested</c> and
	/// <c>ClearPendingRequest</c> were public, documented, and dead — they were not on
	/// <see cref="IEquipmentController"/>, so the panels that send the requests could not reach
	/// them even had they tried.
	/// </para>
	/// <para>
	/// The result was reported from play: dragging an equipped item onto a bank slot put it in the
	/// inventory. The reconcile emptied the equipment slot first and, with no record to consult,
	/// returned the item to the first container with room — the inventory. The acknowledgement then
	/// found the slot already empty and declined to act, so the server held the item in the bank
	/// while the client showed it in the inventory, and nothing ever reconciled the two.
	/// </para>
	/// <para>
	/// The bug was not that the recovery was wrong. It was that the information it needed was never
	/// written down. So these tests pin the calls at the point of request, which is the part that
	/// was missing and the part a later edit would drop again without noticing.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class UnequipDestinationTests
	{
		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}

		/// <summary>Panels that send an unequip, and must record it first.</summary>
		private static readonly string[] UnequipSenders =
		{
			"Assets/Scripts/Client/GUI/World/ItemContainers/UITKItemGridPanel.cs",
			"Assets/Scripts/Client/GUI/World/Inventory/UITKInventory.cs",
			"Assets/Scripts/Client/GUI/World/Equipment/UITKEquipment.cs",
		};

		/// <summary>Panels that send an equip, and must record it first.</summary>
		private static readonly string[] EquipSenders =
		{
			"Assets/Scripts/Client/GUI/World/Inventory/UITKInventory.cs",
			"Assets/Scripts/Client/GUI/World/Equipment/UITKEquipment.cs",
		};

		[Test]
		public void EverySenderRecordsTheUnequipBeforeSendingIt()
		{
			/* "Before" matters as much as "at all": the record has to exist by the time a reconcile
			 * can arrive, and the reconcile can arrive as soon as the request is on the wire. */
			foreach (string path in UnequipSenders)
			{
				string source = ReadSource(path);

				int send = source.IndexOf("new EquipmentUnequipItemBroadcast", StringComparison.Ordinal);
				LogAssert.IsTrue(send >= 0, $"{path} must still send an unequip.");

				int notify = source.IndexOf("NotifyUnequipRequested", StringComparison.Ordinal);

				LogAssert.IsTrue(notify >= 0,
					$"{path} must record the unequip destination, or a reconcile will guess it");
				LogAssert.IsTrue(notify < send,
					$"{path} must record the destination BEFORE the request goes out");
			}
		}

		[Test]
		public void EverySenderRecordsTheEquipBeforeSendingIt()
		{
			foreach (string path in EquipSenders)
			{
				string source = ReadSource(path);

				int send = source.IndexOf("new EquipmentEquipItemBroadcast", StringComparison.Ordinal);
				LogAssert.IsTrue(send >= 0, $"{path} must still send an equip.");

				int notify = source.IndexOf("NotifyEquipRequested", StringComparison.Ordinal);

				LogAssert.IsTrue(notify >= 0,
					$"{path} must record the equip request");
				LogAssert.IsTrue(notify < send,
					$"{path} must record it BEFORE the request goes out");
			}
		}

		[Test]
		public void ThePanelsCanReachTheRecordingMethods()
		{
			/* The reason this went unwired for so long. The methods were public on the concrete
			 * controller but absent from the interface, and the panels only ever hold the
			 * interface — so the calls could not have been written even by someone who knew they
			 * were needed. */
			string contract = ReadSource(
				"Assets/Scripts/Shared/Core/Entity/Item/Container/Equipment/IEquipmentController.cs");

			foreach (string member in new[]
			{
				"NotifyEquipRequested", "NotifyUnequipRequested", "ClearPendingRequest",
			})
			{
				LogAssert.IsTrue(contract.Contains(member),
					$"IEquipmentController must expose {member}, or no panel can call it");
			}
		}

		[Test]
		public void TheReconcileStillPrefersTheRecordedContainer()
		{
			/* The other half of the pair. Recording the destination only helps because the restore
			 * consults it first and falls back afterwards; a restore that ignored the preference
			 * would leave the calls above doing nothing at all. */
			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/Equipment/EquipmentController.cs");

			int restore = source.IndexOf("private void ReturnToAnyContainer", StringComparison.Ordinal);
			LogAssert.IsTrue(restore >= 0, "the reconcile restore must still exist");

			int end = source.IndexOf("private static bool TryReturnTo", restore, StringComparison.Ordinal);
			LogAssert.IsTrue(end > restore, "the restore body must be locatable");

			string body = source.Substring(restore, end - restore);

			LogAssert.IsTrue(body.Contains("preferred.HasValue"),
				"the restore must try the recorded container before falling back");
		}
	}
}
