using UnityEngine;
using FishNet.Object;
using FishNet.Transporting;
using System.Collections.Generic;
#if !UNITY_SERVER
using FishMMO.Client;
#endif

namespace FishMMO.Shared
{
	/// <summary>
	/// Character guild controller.
	/// </summary>
	[RequireComponent(typeof(Character))]
	public class FriendController : NetworkBehaviour
	{
		public Character Character;

		public readonly HashSet<long> Friends = new HashSet<long>();

		public void AddFriend(long friendID)
		{
			if (!Friends.Contains(friendID))
			{
				Friends.Add(friendID);
			}
		}

#if !UNITY_SERVER
		public override void OnStartClient()
		{
			base.OnStartClient();

			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}

			ClientManager.RegisterBroadcast<FriendAddBroadcast>(OnClientFriendAddBroadcastReceived);
			ClientManager.RegisterBroadcast<FriendAddMultipleBroadcast>(OnClientFriendAddMultipleBroadcastReceived);
			ClientManager.RegisterBroadcast<FriendRemoveBroadcast>(OnClientFriendRemoveBroadcastReceived);
		}

		public override void OnStopClient()
		{
			base.OnStopClient();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<FriendAddBroadcast>(OnClientFriendAddBroadcastReceived);
				ClientManager.UnregisterBroadcast<FriendAddMultipleBroadcast>(OnClientFriendAddMultipleBroadcastReceived);
				ClientManager.UnregisterBroadcast<FriendRemoveBroadcast>(OnClientFriendRemoveBroadcastReceived);
			}
		}



		/// <summary>
		/// When we need to add a single friend.
		/// </summary>
		public void OnClientFriendAddBroadcastReceived(FriendAddBroadcast msg, Channel channel)
		{
			if (UIManager.TryGet("UIFriendList", out UIFriendList uiFriendList))
			{
				if (!Friends.Contains(msg.characterID))
				{
					Friends.Add(msg.characterID);
					uiFriendList.OnAddFriend(msg.characterID, msg.online);
				}
			}
		}

		/// <summary>
		/// When we need to add multiple friends.
		/// </summary>
		public void OnClientFriendAddMultipleBroadcastReceived(FriendAddMultipleBroadcast msg, Channel channel)
		{
			if (UIManager.TryGet("UIFriendList", out UIFriendList uiFriendList))
			{
				foreach (FriendAddBroadcast friend in msg.friends)
				{
					if (!Friends.Contains(friend.characterID))
					{
						Friends.Add(friend.characterID);
						uiFriendList.OnAddFriend(friend.characterID, friend.online);
					}
				}
			}
		}

		/// <summary>
		/// When we need to remove a friend.
		/// </summary>
		public void OnClientFriendRemoveBroadcastReceived(FriendRemoveBroadcast msg, Channel channel)
		{
			Friends.Remove(msg.characterID);

			if (UIManager.TryGet("UIFriendList", out UIFriendList uiFriendList))
			{
				uiFriendList.OnRemoveFriend(msg.characterID);
			}
		}
#endif
	}
}