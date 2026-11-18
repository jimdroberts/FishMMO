using System.Collections.Generic;
using FishNet.Connection;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// A dead NPC that players may loot: an interactable whose contents were rolled once, at
	/// death, and are shared between everyone who earned rights to them.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The pile is shared rather than instanced. Taking a slot empties it for every viewer, which
	/// is why the server keeps the viewer list here on the corpse itself: the moment one looter
	/// takes something, everyone else looking at the same corpse has to be told.
	/// </para>
	/// <para>
	/// Slots are never compacted. An emptied slot stays as a hole so that a slot index another
	/// client is already holding never comes to mean a different item — which is exactly the race
	/// that compaction would create between one player's take and another's in-flight request.
	/// </para>
	/// </remarks>
	public interface ILootableCorpse : IInteractable
	{
		/// <summary>
		/// True while the NPC is dead and has not yet decayed.
		/// </summary>
		bool IsCorpse { get; }

		/// <summary>
		/// True while the corpse still holds an item or any currency.
		/// </summary>
		bool HasLoot { get; }

		/// <summary>
		/// The corpse's item slots. Emptied slots read null and keep their index.
		/// </summary>
		IReadOnlyList<Item> LootItems { get; }

		/// <summary>
		/// Currency remaining on the corpse.
		/// </summary>
		long LootCurrency { get; }

		/// <summary>
		/// Returns true if the given character earned loot rights to this corpse.
		/// </summary>
		/// <param name="characterID">The character ID to test.</param>
		bool IsEligibleLooter(long characterID);

		/// <summary>
		/// Removes the item in the given slot and hands it to the caller.
		/// </summary>
		/// <param name="slot">The slot to empty.</param>
		/// <param name="item">Receives the item that was removed.</param>
		/// <returns>True when a item was removed.</returns>
		bool TryTakeLootItem(int slot, out Item item);

		/// <summary>
		/// Puts an item back into a slot it was taken from.
		/// </summary>
		/// <remarks>
		/// The take must be undoable because granting can fail after the corpse has already given
		/// the item up — a full inventory being the ordinary case. Without a way back, that item
		/// is destroyed rather than left on the corpse.
		/// </remarks>
		/// <param name="item">The item to restore.</param>
		/// <param name="slot">The slot to restore it to.</param>
		/// <returns>True when the item was restored.</returns>
		bool ReturnLootItem(Item item, int slot);

		/// <summary>
		/// Takes up to <paramref name="maximum"/> currency from the corpse.
		/// </summary>
		/// <remarks>
		/// Capped rather than all-or-nothing because the destination has a ceiling: a character's
		/// currency is an int, and a taker already near that ceiling must be able to take what
		/// fits and leave the rest on the body rather than either overflowing or being refused.
		/// </remarks>
		/// <param name="maximum">The most the caller can accept. Must be positive.</param>
		/// <param name="amount">Receives the amount actually taken.</param>
		/// <returns>True when any currency was taken.</returns>
		bool TryTakeLootCurrency(long maximum, out long amount);

		/// <summary>
		/// Puts currency back onto the corpse after a failed grant.
		/// </summary>
		/// <param name="amount">The amount to restore.</param>
		void ReturnLootCurrency(long amount);

		/// <summary>
		/// Records that a connection has the loot window open on this corpse.
		/// </summary>
		/// <param name="connection">The viewing connection.</param>
		void AddLootViewer(NetworkConnection connection);

		/// <summary>
		/// Records that a connection has closed the loot window on this corpse.
		/// </summary>
		/// <param name="connection">The connection that closed it.</param>
		void RemoveLootViewer(NetworkConnection connection);

		/// <summary>
		/// Connections currently viewing this corpse's loot.
		/// </summary>
		IReadOnlyCollection<NetworkConnection> LootViewers { get; }

		/// <summary>
		/// Raised on the server when the corpse is about to leave the world, so open loot windows
		/// can be closed before the scene object ID they refer to stops resolving.
		/// </summary>
		event System.Action<ILootableCorpse> OnCorpseExpired;
	}
}
