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
#if UNITY_SERVER
			if (ItemTemplate == null || initiator == null)
			{
				return;
			}

			if (!initiator.TryGet(out IInventoryController inventoryController))
			{
				return;
			}

			int remaining = Amount;
			List<Item> items = inventoryController.Items;
			for (int i = 0; i < items.Count && remaining > 0; i++)
			{
				Item item = items[i];
				if (item != null && item.Template == ItemTemplate)
				{
					inventoryController.RemoveItem(i);
					remaining--;
				}
			}
#endif
		}
	}
}