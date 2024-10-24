﻿using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIFriendList : UIControl
	{
		public RectTransform FriendParent;
		public UIFriend FriendPrefab;
		public Dictionary<long, UIFriend> Friends = new Dictionary<long, UIFriend>();

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
			foreach (UIFriend friend in new List<UIFriend>(Friends.Values))
			{
				friend.FriendID = 0;
				friend.OnRemoveFriend = null;
				Destroy(friend.gameObject);
			}
			Friends.Clear();
		}

		public void OnAddFriend(long friendID, bool online)
		{
			if (FriendPrefab != null && FriendParent != null)
			{
				if (!Friends.TryGetValue(friendID, out UIFriend uiFriend))
				{
					uiFriend = Instantiate(FriendPrefab, FriendParent);
					uiFriend.FriendID = friendID;
					uiFriend.OnRemoveFriend += OnButtonRemoveFriend;
					Friends.Add(friendID, uiFriend);
				}
				if (uiFriend != null)
				{
					if (uiFriend.Name != null)
					{
						ClientNamingSystem.SetName(NamingSystemType.CharacterName, friendID, (n) =>
						{
							uiFriend.Name.text = n;
						});
					}
					if (uiFriend.Status != null)
					{
						uiFriend.Status.text = online ? "Online" : "Offline";
					}
				}
			}
		}

		public void OnRemoveFriend(long friendID)
		{
			if (Friends.TryGetValue(friendID, out UIFriend friend))
			{
				Friends.Remove(friendID);
				friend.FriendID = 0;
				friend.OnRemoveFriend = null;
				Destroy(friend.gameObject);
			}
		}

		private void OnButtonRemoveFriend(long friendID)
		{
			if (UIManager.TryGet("UIConfirmationTooltip", out UIConfirmationTooltip tooltip))
			{
				tooltip.Open("Are you sure you want to remove your friend?", () =>
				{
					Client.NetworkManager.ClientManager.Broadcast(new FriendRemoveBroadcast()
					{
						characterID = friendID,
					});
				}, null);
			}
		}

		public void OnButtonAddFriend()
		{
			if (FriendPrefab != null && FriendParent != null)
			{
				if (UIManager.TryGet("UIInputConfirmationTooltip", out UIInputConfirmationTooltip tooltip))
				{
					tooltip.Open("Who would you like to add as a friend?", (s) =>
					{
						Client.NetworkManager.ClientManager.Broadcast(new FriendAddNewBroadcast()
						{
							characterName = s,
						});
					}, null);
				}
			}
		}
	}
}