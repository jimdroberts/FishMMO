using FishNet.Broadcast;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public struct FriendAddNewBroadcast : IBroadcast
	{
		public long CharacterID;
	}

	public struct FriendAddBroadcast : IBroadcast
	{
		public long CharacterID;
		public bool Online;
	}

	public struct FriendAddMultipleBroadcast : IBroadcast
	{
		public List<FriendAddBroadcast> Friends;
	}

	public struct FriendRemoveBroadcast : IBroadcast
	{
		public long CharacterID;
	}
}