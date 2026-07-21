using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that gives an item to a character's inventory.
	/// Server-only execution.
	/// </summary>
	[Serializable]
	public class GiveItemAction : BaseAction
	{
		/// <summary>
		/// The item template to give.
		/// </summary>
		[Tooltip("The item template to give to the character.")]
		public BaseItemTemplate ItemTemplate;

		/// <summary>
		/// The amount to give.
		/// </summary>
		[Tooltip("The number of items to give.")]
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

			if (initiator is not IPlayerCharacter playerCharacter)
			{
				return;
			}

			if (!initiator.TryGet(out IInventoryController inventoryController))
			{
				return;
			}

			Item item = new Item(ItemTemplate, (uint)Amount);
			inventoryController.TryAddItem(item, out _);
#endif
		}
	}
}