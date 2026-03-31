using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Event data for player interactions, such as talking to NPCs or interacting with objects.
	/// Carries the interactable that was triggered so ECA actions can cast it to the specific type.
	/// </summary>
	public class PlayerInteractionEventData : EventData
	{
		/// <summary>
		/// The interactable object that was interacted with.
		/// ECA actions cast this to the specific interactable interface they require
		/// (e.g., <see cref="IBanker"/>, <see cref="IMerchant"/>).
		/// </summary>
		public IInteractable Interactable { get; }

		/// <summary>
		/// Optional delegate that grants an item to the player's inventory and persists it to the database.
		/// Set by the server (<c>InteractableSystem</c>) when creating this event data.
		/// Returns <c>true</c> if the item was successfully added.
		/// </summary>
		public Func<ICharacter, IInventoryController, Item, bool> OnGrantItem { get; }

		/// <summary>
		/// The GameObject of the interactable (convenience accessor).
		/// </summary>
		public GameObject Target => Interactable?.GameObject;

		/// <summary>
		/// The concrete type name of the interactable, used for logging.
		/// </summary>
		public string InteractionType => Interactable?.GetType().Name ?? "Unknown";

		/// <summary>
		/// Constructs a new PlayerInteractionEventData from the interacting player and the interactable.
		/// </summary>
		/// <param name="initiator">The player character who initiated the interaction.</param>
		/// <param name="interactable">The interactable object that was interacted with.</param>
		/// <param name="onGrantItem">Optional delegate for granting items with DB persistence.</param>
		public PlayerInteractionEventData(IPlayerCharacter initiator, IInteractable interactable, Func<ICharacter, IInventoryController, Item, bool> onGrantItem = null) : base(initiator)
		{
			Interactable = interactable;
			OnGrantItem = onGrantItem;
		}

		/// <summary>
		/// Returns a string representation of the event data for debugging/logging.
		/// </summary>
		public override string ToString() => $"PlayerInteractionEventData (Initiator: {Initiator?.Name}, Target: {Target?.name}, InteractionType: {InteractionType})";
	}
}