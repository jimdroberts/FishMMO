using FishNet.Broadcast;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public struct FriendAddNewBroadcast : IBroadcast
	{
		public long characterID;
	}

	public struct FriendAddBroadcast : IBroadcast
	{
		public long characterID;
		public bool online;
	}

	public struct FriendAddMultipleBroadcast : IBroadcast
	{
		public List<FriendAddBroadcast> friends;
	}

	public struct FriendRemoveBroadcast : IBroadcast
	{
		public long characterID;
	}
}