using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for friend controllers, providing access and management for a character's friend list.
	/// Used to add, remove, and track friends, and handle related events.
	/// </summary>
	public interface IFriendController : ICharacterBehaviour
	{
		/// <summary>
		/// Event invoked when a friend is added. Parameters: friend ID, online status.
		/// </summary>
		event Action<long, bool> OnAddFriend;

		/// <summary>
		/// Event invoked when a friend is removed. Parameter: friend ID.
		/// </summary>
		event Action<long> OnRemoveFriend;

		/// <summary>
		/// Set of friend IDs for this character.
		/// </summary>
		HashSet<long> Friends { get; }

		/// <summary>
		/// Adds a friend by ID if not already present.
		/// </summary>
		/// <param name="friendID">The ID of the friend to add.</param>
		void AddFriend(long friendID);
	}
}