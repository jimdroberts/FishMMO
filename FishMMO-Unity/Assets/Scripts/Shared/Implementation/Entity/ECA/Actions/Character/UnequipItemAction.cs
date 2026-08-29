using System;
using System.Collections.Generic;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that attempts to unequip an item from the initiating character.
	/// Optionally uses <see cref="ItemEventData"/> from the <see cref="EventData"/> to override the target slot and container.
	/// Server-only execution — equipment mutations rearrange persistent item ownership
	/// between containers and must never run during client prediction replay.
	/// </summary>
	[Serializable]
	public class UnequipItemAction : BaseAction
	{
		/// <summary>
		/// The default equipment slot to unequip from, used when no <see cref="ItemEventData"/> is provided.
		/// </summary>
		public ItemSlot SourceSlotToUnequip = ItemSlot.Head;

		/// <summary>
		/// Unequips the item from the specified slot on the initiator's <see cref="IEquipmentController"/>.
		/// </summary>
		/// <param name="initiator">The character performing the unequip.</param>
		/// <param name="eventData">Optional event data containing <see cref="ItemEventData"/> to override slot and target container.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			/* Server only, decided at runtime rather than by a build define.
			 *
			 * This body was wrapped in `#if UNITY_SERVER`, which is a BUILD TARGET define and is
			 * undefined in the editor the scene server is developed in — so the action compiled away
			 * entirely there and did nothing on the server either. That failure is invisible: the
			 * action still exists, still serialises, and its trigger still fires; it simply never has
			 * an effect, which reads as "the quest/item/achievement hook is broken" rather than as a
			 * build-configuration problem. EcaAuthority asks the question that was meant all along,
			 * of the peer the character actually belongs to. See EcaAuthority's own remarks. */
			if (!EcaAuthority.IsServer(initiator, eventData))
			{
				return;
			}

			if (initiator == null)
			{
				Log.Warning("UnequipItemAction", "Initiator is null. Cannot execute action.");
				return;
			}

			if (!initiator.TryGet(out IEquipmentController equipmentController))
			{
				Log.Warning("UnequipItemAction", "Initiator does not have an IEquipmentController.");
				return;
			}

			ItemSlot unequipSlot = SourceSlotToUnequip;
			IItemContainer targetContainer = null;

			if (eventData != null && eventData.TryGet(out ItemEventData itemEventData))
			{
				if (itemEventData.TargetSlot != unequipSlot)
				{
					unequipSlot = itemEventData.TargetSlot;
				}
				targetContainer = itemEventData.SourceContainer;
			}

			if (targetContainer == null)
			{
				Log.Warning("UnequipItemAction", "No target container specified to unequip item to. Aborting.");
				return;
			}

			bool success = equipmentController.Unequip(targetContainer, (byte)unequipSlot, out List<Item> modifiedItems);

			if (success)
			{
				Log.Debug("UnequipItemAction", $"Successfully unequipped item from {unequipSlot}.");
			}
			else
			{
				Log.Warning("UnequipItemAction", $"Failed to unequip item from {unequipSlot}.");
			}
		}
	}
}
