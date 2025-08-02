using FishNet.Transporting;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI control for managing and displaying the player's friend list.
	/// </summary>
	public class UIFriendList : UICharacterControl
	{
		/// <summary>
		/// The parent transform for friend UI elements.
		/// </summary>
		public RectTransform FriendParent;
		/// <summary>
		/// Prefab used to instantiate friend UI elements.
		/// </summary>
		public UIFriend FriendPrefab;
		/// <summary>
		/// Dictionary of friends by character ID.
		/// </summary>
		public Dictionary<long, UIFriend> Friends = new Dictionary<long, UIFriend>();

		/// <summary>
		/// Called when the UI is being destroyed. Cleans up friend UI elements.
		/// </summary>
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

		/// <summary>
		/// Called after setting the character reference. Subscribes to friend controller events.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character.TryGet(out IFriendController friendController))
			{
				friendController.OnAddFriend += FriendController_OnAddFriend;
				friendController.OnRemoveFriend += FriendController_OnRemoveFriend;
			}
		}

		/// <summary>
		/// Called before unsetting the character reference. Unsubscribes from friend controller events.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			if (Character.TryGet(out IFriendController friendController))
			{
				friendController.OnAddFriend -= FriendController_OnAddFriend;
				friendController.OnRemoveFriend -= FriendController_OnRemoveFriend;
			}
		}

		/// <summary>
		/// Handles adding a friend and updates the friend list UI.
		/// </summary>
		/// <param name="friendID">The character ID of the friend.</param>
		/// <param name="online">Whether the friend is online.</param>
		public void FriendController_OnAddFriend(long friendID, bool online)
		{
			if (FriendPrefab != null && FriendParent != null)
			{
				if (!Friends.TryGetValue(friendID, out UIFriend uiFriend))
				{
					uiFriend = Instantiate(FriendPrefab, FriendParent);
					uiFriend.FriendID = friendID;
					uiFriend.OnRemoveFriend += OnButtonRemoveFriend;
					uiFriend.gameObject.SetActive(true);
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

		/// <summary>
		/// Handles removing a friend from the friend list UI.
		/// </summary>
		/// <param name="friendID">The character ID of the friend to remove.</param>
		public void FriendController_OnRemoveFriend(long friendID)
		{
			if (Friends.TryGetValue(friendID, out UIFriend friend))
			{
				Friends.Remove(friendID);
				friend.FriendID = 0;
				friend.OnRemoveFriend = null;
				Destroy(friend.gameObject);
			}
		}

		/// <summary>
		/// Handles the remove friend button click. Prompts for confirmation and sends a remove request.
		/// </summary>
		/// <param name="friendID">The character ID of the friend to remove.</param>
		private void OnButtonRemoveFriend(long friendID)
		{
			if (UIManager.TryGet("UIDialogBox", out UIDialogBox tooltip))
			{
				tooltip.Open("Are you sure you want to remove your friend?", () =>
				{
					Client.Broadcast(new FriendRemoveBroadcast()
					{
						CharacterID = friendID,
					}, Channel.Reliable);
				}, () => { });
			}
		}

		/// <summary>
		/// Handles the add friend button click. Prompts for a name and sends an add request.
		/// </summary>
		public void OnButtonAddFriend()
		{
			if (FriendPrefab != null && FriendParent != null)
			{
				if (UIManager.TryGet("UIDialogInputBox", out UIDialogInputBox tooltip))
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
											CharacterID = id
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