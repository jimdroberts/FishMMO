using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that removes items from a character's inventory by template.
	/// Server-only execution.
	/// </summary>
	[Serializable]
	public class RemoveItemAction : BaseAction
	{
		/// <summary>
		/// The item template to remove.
		/// </summary>
		[Tooltip("The item template to remove from the character.")]
		public BaseItemTemplate ItemTemplate;

		/// <summary>
		/// The amount to remove.
		/// </summary>
		[Tooltip("The number of items to remove.")]
		[Min(1)]
		public int Amount = 1;

		/// <inheritdoc />
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

			if (ItemTemplate == null || initiator == null)
			{
				return;
			}

			if (!initiator.TryGet(out IInventoryController inventoryController))
			{
				return;
			}

			int remaining = Amount;
			int targetTemplateID = ItemTemplate.ID;
			List<Item> items = inventoryController.Items;
			for (int i = 0; i < items.Count && remaining > 0; i++)
			{
				Item item = items[i];
				if (item == null || item.Template == null)
				{
					continue;
				}

				// Compare by template ID, not reference equality.
				// Templates loaded from different Addressable paths may not be
				// reference-equal even when they represent the same template.
				if (item.Template.ID != targetTemplateID)
				{
					continue;
				}

				if (item.IsStackable && item.Stackable != null)
				{
					// For stackable items, reduce the stack amount rather than
					// destroying the entire item. Removing the whole stack when
					// only a portion is needed causes massive item loss.
					uint stackAmount = item.Stackable.Amount;
					if (stackAmount <= (uint)remaining)
					{
						inventoryController.RemoveItem(i);
						remaining -= (int)stackAmount;
					}
					else
					{
						item.Stackable.Amount -= (uint)remaining;
						remaining = 0;
					}
				}
				else
				{
					inventoryController.RemoveItem(i);
					remaining--;
				}
			}
		}
	}
}