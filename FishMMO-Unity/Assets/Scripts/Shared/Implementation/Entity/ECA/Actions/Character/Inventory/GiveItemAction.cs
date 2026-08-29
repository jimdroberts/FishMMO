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
		}
	}
}