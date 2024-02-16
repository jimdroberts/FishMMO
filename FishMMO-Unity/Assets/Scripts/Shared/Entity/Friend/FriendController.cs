using FishNet.Transporting;
using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Character guild controller.
	/// </summary>
	public class FriendController : CharacterBehaviour
	{
		public readonly HashSet<long> Friends = new HashSet<long>();

		public Action<long, bool> OnAddFriend;
		public Action<long> OnRemoveFriend;

		public void AddFriend(long friendID)
		{
			if (!Friends.Contains(friendID))
			{
				Friends.Add(friendID);
			}
		}

#if !UNITY_SERVER
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}

			ClientManager.RegisterBroadcast<FriendAddBroadcast>(OnClientFriendAddBroadcastReceived);
			ClientManager.RegisterBroadcast<FriendAddMultipleBroadcast>(OnClientFriendAddMultipleBroadcastReceived);
			ClientManager.RegisterBroadcast<FriendRemoveBroadcast>(OnClientFriendRemoveBroadcastReceived);
		}

		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

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
			if (!Friends.Contains(msg.characterID))
			{
				Friends.Add(msg.characterID);

				OnAddFriend?.Invoke(msg.characterID, msg.online);
			}
		}

		/// <summary>
		/// When we need to add multiple friends.
		/// </summary>
		public void OnClientFriendAddMultipleBroadcastReceived(FriendAddMultipleBroadcast msg, Channel channel)
		{
			foreach (FriendAddBroadcast friend in msg.friends)
			{
				if (!Friends.Contains(friend.characterID))
				{
					Friends.Add(friend.characterID);

					OnAddFriend?.Invoke(friend.characterID, friend.online);
				}
			}
		}

		/// <summary>
		/// When we need to remove a friend.
		/// </summary>
		public void OnClientFriendRemoveBroadcastReceived(FriendRemoveBroadcast msg, Channel channel)
		{
			Friends.Remove(msg.characterID);

			OnRemoveFriend?.Invoke(msg.characterID);
		}
#endif
	}
}