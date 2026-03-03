using System;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;

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
		/// Swaps two item slots within the same container and collects the affected items.
		/// </summary>
		/// <param name="container">The container instance in which the swap occurs.</param>
		/// <param name="fromIndex">Source slot index.</param>
		/// <param name="toIndex">Target slot index.</param>
		/// <param name="affectedItems">Out: list of items whose slot assignments changed and need persistence.</param>
		/// <returns>True when the swap succeeded; otherwise false.</returns>
		bool SwapContainerItems(IItemContainer container, int fromIndex, int toIndex, out List<Item> affectedItems);

		/// <summary>
		/// Swaps items between two containers and collects the affected items along
		/// with any slot deletions that occurred during the cross-container move.
		/// </summary>
		/// <param name="from">Source container.</param>
		/// <param name="to">Destination container.</param>
		/// <param name="fromIndex">Source slot index.</param>
		/// <param name="toIndex">Destination slot index.</param>
		/// <param name="affectedFromItems">Out: items placed into the source container that need persistence.</param>
		/// <param name="deletedFromSlots">Out: slot indices that were vacated in the source container and need deletion.</param>
		/// <param name="affectedToItems">Out: items placed into the destination container that need persistence.</param>
		/// <returns>True when the cross-container swap succeeded; otherwise false.</returns>
		bool SwapContainerItems(IItemContainer from, IItemContainer to, int fromIndex, int toIndex,
			out List<Item> affectedFromItems, out List<long> deletedFromSlots, out List<Item> affectedToItems);
	}
}