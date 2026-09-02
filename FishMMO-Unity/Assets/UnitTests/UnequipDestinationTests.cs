using System;
using System.IO;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that every equip and unequip request reaches the server as replicate input, never as
	/// a broadcast, and that the panels have no other way to send one.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The reliable acknowledgement was the bug. A broadcast is applied the moment it is parsed,
	/// while the state updates for the ticks before the server processed the request are still
	/// queued behind it — FishNet holds each one until it is a few ticks old. So every equip was
	/// undone by a stale snapshot and re-done by the next (the item "in the inventory briefly,
	/// then equipped"), and every unequip was re-equipped by the stale snapshot and then dropped
	/// into the first container with room by the next (the item "in the inventory and stuck",
	/// because the server had it in the bank). Recording the destination before sending, which
	/// this fixture used to pin, could not fix it: the record was consumed by the acknowledgement
	/// before the stale snapshot arrived.
	/// </para>
	/// <para>
	/// A request that rides the replicate has no such race. It is applied on the same tick on
	/// both peers, a snapshot for an earlier tick restores the earlier state and the replay
	/// re-applies the request, and a snapshot at or past the tick is the verdict. These tests pin
	/// the shape of that: the panels queue through the controller, the controller's queue empties
	/// into the replicate input, and no equip broadcast type exists to be sent instead.
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

		/// <summary>Every panel that can start an equip or an unequip.</summary>
		private static readonly string[] Panels =
		{
			"Assets/Scripts/Client/GUI/World/ItemContainers/UITKItemGridPanel.cs",
			"Assets/Scripts/Client/GUI/World/Equipment/UITKEquipment.cs",
			"Assets/Scripts/Client/GUI/World/Inventory/UITKInventory.cs",
		};

		[Test]
		public void NoPanelBroadcastsAnEquipmentRequest()
		{
			foreach (string path in Panels)
			{
				string source = ReadSource(path);
				LogAssert.IsFalse(source.Contains("new EquipmentEquipItemBroadcast"),
					$"{path} must not send an equip as a broadcast; it is replicate input.");
				LogAssert.IsFalse(source.Contains("new EquipmentUnequipItemBroadcast"),
					$"{path} must not send an unequip as a broadcast; it is replicate input.");
			}
		}

		[Test]
		public void TheEquipBroadcastTypeNoLongerExists()
		{
			/* The type itself is gone, so a future panel cannot reach for it. The unequip message
			 * survives, server-to-owner only, to report where the item landed. */
			string source = ReadSource("Assets/Scripts/Shared/Implementation/Network/Character/Inventory/EquipmentBroadcasts.cs");
			LogAssert.IsFalse(source.Contains("struct EquipmentEquipItemBroadcast"),
				"the equip acknowledgement must not come back; see the file's remarks");
			LogAssert.IsTrue(source.Contains("struct EquipmentUnequipItemBroadcast"),
				"the unequip destination message must still exist");
			LogAssert.IsTrue(source.Contains("public long ItemID"),
				"the unequip destination must name the item by identity, not by where the owner put it");
		}

		[Test]
		public void EveryPanelQueuesThroughTheController()
		{
			string grid = ReadSource(Panels[0]);
			string equipment = ReadSource(Panels[1]);
			string inventory = ReadSource(Panels[2]);

			LogAssert.IsTrue(grid.Contains("RequestUnequip("), "the grid panels unequip through IEquipmentController.RequestUnequip");
			LogAssert.IsTrue(equipment.Contains("RequestEquip("), "the equipment panel equips through IEquipmentController.RequestEquip");
			LogAssert.IsTrue(equipment.Contains("RequestUnequip("), "the equipment panel unequips through IEquipmentController.RequestUnequip");
			LogAssert.IsTrue(inventory.Contains("RequestEquip("), "the inventory right-click equips through IEquipmentController.RequestEquip");
		}

		[Test]
		public void ThePanelsCanReachTheRequestMethods()
		{
			/* The panels only ever hold the interface, so the methods have to be on it. */
			string contract = ReadSource(
				"Assets/Scripts/Shared/Core/Entity/Item/Container/Equipment/IEquipmentController.cs");

			foreach (string member in new[] { "RequestEquip", "RequestUnequip", "OnRequestResolved", "ApplyUnequipDestination" })
			{
				LogAssert.IsTrue(contract.Contains(member),
					$"IEquipmentController must expose {member}, or no panel can call it");
			}
		}

		[Test]
		public void TheRequestRidesTheReplicate()
		{
			string data = ReadSource("Assets/Scripts/Shared/Implementation/Entity/Prediction/CharacterReplicateData.cs");
			LogAssert.IsTrue(data.Contains("public byte EquipmentRequest"), "CharacterReplicateData must carry the packed request");
			LogAssert.IsTrue(data.Contains("public short EquipmentIndex"), "CharacterReplicateData must carry the source index");

			string controller = ReadSource("Assets/Scripts/Shared/Implementation/Entity/Prediction/Equipment/EquipmentController.cs");
			int populate = controller.IndexOf("public void PopulateInput(ref CharacterReplicateData input)", StringComparison.Ordinal);
			int replicate = controller.IndexOf("public void OnReplicate(ref CharacterReplicateData input", StringComparison.Ordinal);
			LogAssert.IsTrue(populate >= 0 && replicate > populate, "the controller must populate and consume the replicate input");

			string populateBody = controller.Substring(populate, replicate - populate);
			LogAssert.IsTrue(populateBody.Contains("input.EquipmentRequest = packed"),
				"PopulateInput must write the queued request into the replicate");
		}

		[Test]
		public void TheRestoreReturnsAPredictedItemToItsOrigin()
		{
			/* The half that makes a stale snapshot harmless. The restore consults the recorded
			 * origin before falling back; a restore that guessed would leave the replayed request
			 * looking at an empty slot. */
			string source = ReadSource(
				"Assets/Scripts/Shared/Implementation/Entity/Prediction/Equipment/EquipmentController.cs");

			int restore = source.IndexOf("private void RemoveFromSlotForReconcile", StringComparison.Ordinal);
			LogAssert.IsTrue(restore >= 0, "the reconcile restore must still exist");

			int end = source.IndexOf("private void DetachFromSlot", restore, StringComparison.Ordinal);
			LogAssert.IsTrue(end > restore, "the restore body must be locatable");

			string body = source.Substring(restore, end - restore);
			LogAssert.IsTrue(body.Contains("TryReturnToPredictedOrigin"),
				"the restore must try the recorded origin before falling back to any container");
		}
	}
}
