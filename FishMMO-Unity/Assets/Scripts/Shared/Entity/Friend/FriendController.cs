using FishNet.Transporting;
using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Character guild controller.
	/// </summary>
	public class FriendController : CharacterBehaviour, IFriendController
	{
		public event Action<long, bool> OnAddFriend;
		public event Action<long> OnRemoveFriend;

		public HashSet<long> Friends { get; private set; }

		public override void OnAwake()
		{
			Friends = new HashSet<long>();
		}

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
			if (!Friends.Contains(msg.CharacterID))
			{
				Friends.Add(msg.CharacterID);

				OnAddFriend?.Invoke(msg.CharacterID, msg.Online);
			}
		}

		/// <summary>
		/// When we need to add multiple friends.
		/// </summary>
		public void OnClientFriendAddMultipleBroadcastReceived(FriendAddMultipleBroadcast msg, Channel channel)
		{
			foreach (FriendAddBroadcast friend in msg.Friends)
			{
				if (!Friends.Contains(friend.CharacterID))
				{
					Friends.Add(friend.CharacterID);

					OnAddFriend?.Invoke(friend.CharacterID, friend.Online);
				}
			}
		}

		/// <summary>
		/// When we need to remove a friend.
		/// </summary>
		public void OnClientFriendRemoveBroadcastReceived(FriendRemoveBroadcast msg, Channel channel)
		{
			Friends.Remove(msg.CharacterID);

			OnRemoveFriend?.Invoke(msg.CharacterID);
		}
#endif
	}
}