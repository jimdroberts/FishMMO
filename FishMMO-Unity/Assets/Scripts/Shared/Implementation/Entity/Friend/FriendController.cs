using FishNet.Transporting;
using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

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
		/// <summary>
		/// Called when the character is started on the client. Registers broadcast listeners for friend add/remove updates.
		/// </summary>
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

		/// <summary>
		/// Called when the character is stopped on the client. Unregisters friend update listeners.
		/// </summary>
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
		/// Handles a broadcast from the server to add a single friend to the list.
		/// </summary>
		/// <param name="msg">The friend add message containing character ID and online status.</param>
		/// <param name="channel">The network channel.</param>
		public void OnClientFriendAddBroadcastReceived(FriendAddBroadcast msg, Channel channel)
		{
			if (!Friends.Contains(msg.CharacterID))
			{
				Friends.Add(msg.CharacterID);

				OnAddFriend?.Invoke(msg.CharacterID, msg.Online);
			}
		}

		/// <summary>
		/// Handles a broadcast from the server to add multiple friends to the list.
		/// </summary>
		/// <param name="msg">The multiple friend add message containing a list of friends.</param>
		/// <param name="channel">The network channel.</param>
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
		/// Handles a broadcast from the server to remove a friend from the list.
		/// </summary>
		/// <param name="msg">The friend remove message containing the character ID to remove.</param>
		/// <param name="channel">The network channel.</param>
		public void OnClientFriendRemoveBroadcastReceived(FriendRemoveBroadcast msg, Channel channel)
		{
			Friends.Remove(msg.CharacterID);

			OnRemoveFriend?.Invoke(msg.CharacterID);
		}
#endif
	}
}