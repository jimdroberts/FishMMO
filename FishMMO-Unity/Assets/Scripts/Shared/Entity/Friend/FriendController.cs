using FishNet.Transporting;
using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Character friend controller. Manages the player's friend list and handles friend-related network events.
	/// </summary>
	public class FriendController : CharacterBehaviour, IFriendController
	{
		/// <summary>
		/// Event invoked when a friend is added. Parameters: friend ID, online status.
		/// </summary>
		public event Action<long, bool> OnAddFriend;

		/// <summary>
		/// Event invoked when a friend is removed. Parameter: friend ID.
		/// </summary>
		public event Action<long> OnRemoveFriend;

		/// <summary>
		/// Set of friend IDs for this character.
		/// </summary>
		public HashSet<long> Friends { get; private set; }

		/// <summary>
		/// Initializes the friend list when the component awakens.
		/// </summary>
		public override void OnAwake()
		{
			Friends = new HashSet<long>();
		}

		/// <summary>
		/// Resets the friend list state, clearing all friends.
		/// </summary>
		/// <param name="asServer">True if called on the server.</param>
		public override void ResetState(bool asServer)
		{
			base.ResetState(asServer);
			Friends.Clear();
		}

		/// <summary>
		/// Adds a friend by ID if not already present.
		/// </summary>
		/// <param name="friendID">The ID of the friend to add.</param>
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