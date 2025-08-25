using System;
using FishMMO.Shared;
using FishMMO.Database.Npgsql;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Engine-agnostic public API for character inventory operations.
	/// Implementations perform item container manipulations and coordinate any
	/// necessary database updates or client notifications.
	/// </summary>
	public interface ICharacterInventorySystem : IServerBehaviour
	{
		/// <summary>
		/// Swaps two item slots within the same container and invokes a callback for
		/// each affected item so the caller can persist the change to the database.
		/// </summary>
		/// <param name="dbContext">Database context used for persistence operations.</param>
		/// <param name="characterID">ID of the character that owns the container.</param>
		/// <param name="container">The container instance in which the swap occurs.</param>
		/// <param name="fromIndex">Source slot index.</param>
		/// <param name="toIndex">Target slot index.</param>
		/// <param name="onDatabaseUpdateSlot">Callback invoked for each item that needs to be persisted. The callback
		/// receives (dbContext, characterID, item).</param>
		/// <returns>True when the swap succeeded; otherwise false.</returns>
		bool SwapContainerItems(NpgsqlDbContext dbContext, long characterID, IItemContainer container, int fromIndex, int toIndex, Action<NpgsqlDbContext, long, Item> onDatabaseUpdateSlot);

		/// <summary>
		/// Swaps items between two containers and invokes optional callbacks to update
		/// the database for removed/added/updated item slots. This overload supports
		/// moving items across different containers (inventory, bank, equipment, etc.).
		/// </summary>
		/// <param name="dbContext">Database context used for persistence operations.</param>
		/// <param name="characterID">ID of the character that owns the containers.</param>
		/// <param name="from">Source container.</param>
		/// <param name="to">Destination container.</param>
		/// <param name="fromIndex">Source slot index.</param>
		/// <param name="toIndex">Destination slot index.</param>
		/// <param name="onDatabaseSetOldSlot">Optional callback invoked to persist an item that was placed into the old slot.</param>
		/// <param name="onDatabaseDeleteOldSlot">Optional callback invoked to delete an old slot entry in the database (signature: dbContext, characterID, slotIndex).</param>
		/// <param name="onDatabaseSetNewSlot">Optional callback invoked to persist the item placed into the new slot.</param>
		/// <returns>True when the cross-container swap succeeded; otherwise false.</returns>
		bool SwapContainerItems(NpgsqlDbContext dbContext, long characterID, IItemContainer from, IItemContainer to, int fromIndex, int toIndex,
			Action<NpgsqlDbContext, long, Item> onDatabaseSetOldSlot = null,
			Action<NpgsqlDbContext, long, long> onDatabaseDeleteOldSlot = null,
			Action<NpgsqlDbContext, long, Item> onDatabaseSetNewSlot = null);
	}
}