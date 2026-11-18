using System;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for crafting an ability using an interactable object.
	/// Contains the interactable object's ID, template ID, and a list of event IDs.
	/// </summary>
	public struct AbilityCraftBroadcast : IBroadcast
	{
		/// <summary>ID of the interactable object used for crafting.</summary>
		public long InteractableID;
		/// <summary>Template ID of the ability to craft.</summary>
		public int TemplateID;
		/// <summary>List of event IDs associated with the crafting process.</summary>
		public int[] Events;
	}

	/// <summary>
	/// Server → Client answer to an <see cref="AbilityCraftBroadcast"/>.
	/// </summary>
	/// <remarks>
	/// Crafting had no reply of any kind. Every refusal in
	/// <c>OnServerAbilityCraftBroadcastReceived</c> was a bare <c>return</c>, so the crafting panel
	/// could only tell a refusal from a slow server by waiting for its own watchdog to expire — the
	/// same defect the merchant buy path had before <c>MerchantPurchaseResultBroadcast</c> existed,
	/// and it reads to the player as a button that does nothing. Every exit from the craft handler
	/// now sends one of these.
	/// </remarks>
	public struct AbilityCraftResultBroadcast : IBroadcast
	{
		/// <summary>Template ID the request named.</summary>
		public int TemplateID;
		/// <summary>True when the ability was crafted.</summary>
		public bool Success;
		/// <summary>Why the craft was refused. <see cref="AbilityCraftFailure.None"/> on success.</summary>
		public AbilityCraftFailure Failure;
		/// <summary>Currency actually charged.</summary>
		public long Charged;
	}

	/// <summary>Why an ability craft was refused.</summary>
	public enum AbilityCraftFailure : byte
	{
		/// <summary>No failure; the ability was crafted.</summary>
		None = 0,
		/// <summary>The character, the crafter or the scene no longer validates.</summary>
		Unavailable,
		/// <summary>The character cannot act right now — dead, stunned, or otherwise gated.</summary>
		CannotAct,
		/// <summary>The template ID did not resolve to an ability.</summary>
		InvalidEntry,
		/// <summary>The character has not learned that ability template.</summary>
		NotKnown,
		/// <summary>The character already holds a crafted ability built from that template.</summary>
		AlreadyCrafted,
		/// <summary>The character holds as many abilities as the server allows.</summary>
		AbilityLimit,
		/// <summary>
		/// The selected events are not craftable together: too many for the template, duplicated,
		/// unknown to the character, or more than one ability-type override.
		/// </summary>
		InvalidEvents,
		/// <summary>The character cannot afford the craft.</summary>
		InsufficientFunds,
		/// <summary>Another request from this connection is still being handled.</summary>
		Busy,
		/// <summary>The craft was accepted but could not be recorded, so nothing was granted.</summary>
		PersistFailed,
		/// <summary>
		/// An effect does not work on that kind of ability: an ordinary effect on a summon spell, or a
		/// summon effect on anything else. See <see cref="AbilityEvent.IsCompatibleWith"/>.
		/// </summary>
		IncompatibleEvents,
	}
}
