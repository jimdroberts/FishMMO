using FishNet.Transporting;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIFriendList : UICharacterControl
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

		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character.TryGet(out FriendController friendController))
			{
				friendController.OnAddFriend += OnAddFriend;
				friendController.OnRemoveFriend += OnRemoveFriend;
			}
		}

		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			if (Character.TryGet(out FriendController friendController))
			{
				friendController.OnAddFriend -= OnAddFriend;
				friendController.OnRemoveFriend -= OnRemoveFriend;
			}
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
					Client.Broadcast(new FriendRemoveBroadcast()
					{
						characterID = friendID,
					}, Channel.Reliable);
				}, null);
			}
		}

		public void OnButtonAddFriend()
		{
			if (FriendPrefab != null && FriendParent != null)
			{
				if (UIManager.TryGet("UIInputConfirmationTooltip", out UIInputConfirmationTooltip tooltip))
				{
					tooltip.Open("Please type the name of the person you wish to add.", (s) =>
					{
						if (Constants.Authentication.IsAllowedCharacterName(s))
                        {
							ClientNamingSystem.GetCharacterID(s, (id) =>
							{
								if (id != 0)
								{
									if (Character.ID != id)
									{
										Client.Broadcast(new FriendAddNewBroadcast()
										{
											characterID = id
										}, Channel.Reliable);
									}
									else if (UIManager.TryGet("UIChat", out UIChat chat))
									{
										chat.InstantiateChatMessage(ChatChannel.System, "", "You can't add yourself as a friend.");
									}
								}
								else if (UIManager.TryGet("UIChat", out UIChat chat))
								{
									chat.InstantiateChatMessage(ChatChannel.System, "", "A person with that name could not be found.");
								}
							});
						}
					}, null);
				}
			}
		}
	}
}