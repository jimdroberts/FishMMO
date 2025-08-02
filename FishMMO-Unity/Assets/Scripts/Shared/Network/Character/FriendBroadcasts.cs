using FishNet.Broadcast;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Broadcast for adding a new friend to a character's friend list.
	/// Contains the character ID of the new friend.
	/// </summary>
	public struct FriendAddNewBroadcast : IBroadcast
	{
		/// <summary>Character ID of the new friend to add.</summary>
		public long CharacterID;
	}

	/// <summary>
	/// Broadcast for adding a friend to the friend list, including online status.
	/// </summary>
	public struct FriendAddBroadcast : IBroadcast
	{
		/// <summary>Character ID of the friend to add.</summary>
		public long CharacterID;
		/// <summary>Whether the friend is currently online.</summary>
		public bool Online;
	}

	/// <summary>
	/// Broadcast for adding multiple friends to the friend list at once.
	/// Used for bulk friend addition or synchronization.
	/// </summary>
	public struct FriendAddMultipleBroadcast : IBroadcast
	{
		/// <summary>List of friends to add.</summary>
		public List<FriendAddBroadcast> Friends;
	}

	/// <summary>
	/// Broadcast for removing a friend from the friend list.
	/// Contains the character ID of the friend to remove.
	/// </summary>
	public struct FriendRemoveBroadcast : IBroadcast
	{
		/// <summary>Character ID of the friend to remove.</summary>
		public long CharacterID;
	}
}