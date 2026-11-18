using System.Collections.Generic;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// An arena board: the object a player interacts with to queue for arenas.
	/// </summary>
	/// <remarks>
	/// The arena counterpart of <see cref="IDungeonEntrance"/>. One board may offer several
	/// arenas. Queuing is done standing at the board and stays valid only while the player
	/// remains near it, exactly as the dungeon group finder ties a waiter to the entrance.
	/// </remarks>
	public interface IArenaBoard : IInteractable
	{
		/// <summary>Template IDs of the arenas this board offers, in display order.</summary>
		IReadOnlyList<int> ArenaTemplateIDs { get; }

		/// <summary>Whether the board offers an arena.</summary>
		bool Offers(int arenaTemplateID);
	}
}
