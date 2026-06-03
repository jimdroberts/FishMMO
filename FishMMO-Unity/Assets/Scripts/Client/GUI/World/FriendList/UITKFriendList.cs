using System.Collections.Generic;
using UnityEngine.UIElements;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit friend list. Replaces the legacy UGUI <see cref="UIFriendList"/> / <see cref="UIFriend"/>:
	/// renders friends as dynamic rows with online status and a remove button, and handles add/remove
	/// actions. All friend broadcasts are preserved verbatim and the shared UGUI dialog overlays are reused.
	/// </summary>
	public class UITKFriendList : UITKCharacterControl
	{
		/// <summary>Name of the container that holds the generated friend rows.</summary>
		private const string FRIEND_LIST_NAME = "friend-list";

		/// <summary>Name of the add-friend button.</summary>
		private const string ADD_BUTTON_NAME = "friend-add";

		/// <summary>USS class applied to each generated friend row.</summary>
		private const string ROW_CLASS = "friend-row";

		/// <summary>USS class applied to a friend row's name label.</summary>
		private const string ROW_NAME_CLASS = "friend-row__name";

		/// <summary>USS class applied to a friend row's status label.</summary>
		private const string ROW_STATUS_CLASS = "friend-row__status";

		/// <summary>USS class applied to a friend row's remove button.</summary>
		private const string ROW_REMOVE_CLASS = "friend-row__remove";

		/// <summary>
		/// Visual elements and state backing a single friend row.
		/// </summary>
		private sealed class FriendRow
		{
			/// <summary>Root container for the row.</summary>
			public VisualElement Root;
			/// <summary>Friend name label.</summary>
			public Label Name;
			/// <summary>Friend online/offline status label.</summary>
			public Label Status;
			/// <summary>The friend's character ID.</summary>
			public long FriendID;
		}

		private readonly Dictionary<long, FriendRow> friends = new Dictionary<long, FriendRow>();
		private VisualElement friendList;

		/// <summary>
		/// Queries the friend list and wires up the add-friend button.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			friendList = root.Q(FRIEND_LIST_NAME);

			Button add = root.Q<Button>(ADD_BUTTON_NAME);
			if (add != null)
			{
				add.clicked += OnButtonAddFriend;
			}
		}

		/// <summary>
		/// Clears all friend rows when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			ClearAll();
		}

		/// <summary>
		/// Subscribes to friend controller events after the character is set.
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
		/// Unsubscribes from friend controller events before the character is unset.
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
		/// Adds or updates a friend row with the latest name and online status.
		/// </summary>
		/// <param name="friendID">The friend's character ID.</param>
		/// <param name="online">Whether the friend is online.</param>
		public void FriendController_OnAddFriend(long friendID, bool online)
		{
			if (friendList == null)
			{
				return;
			}

			FriendRow row = GetOrCreateRow(friendID);

			ClientNamingSystem.SetName(NamingSystemType.CharacterName, friendID, (n) =>
			{
				row.Name.text = n;
			});

			row.Status.text = online ? "Online" : "Offline";
			row.Status.EnableInClassList("friend-row__status--online", online);
		}

		/// <summary>
		/// Removes a friend row.
		/// </summary>
		/// <param name="friendID">The friend's character ID.</param>
		public void FriendController_OnRemoveFriend(long friendID)
		{
			if (friends.TryGetValue(friendID, out FriendRow row))
			{
				row.Root?.RemoveFromHierarchy();
				friends.Remove(friendID);
			}
		}

		/// <summary>
		/// Confirms then broadcasts a remove-friend request.
		/// </summary>
		/// <param name="friendID">The friend's character ID.</param>
		private void OnButtonRemoveFriend(long friendID)
		{
			if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox tooltip))
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
		/// Prompts for a name and broadcasts an add-friend request.
		/// </summary>
		public void OnButtonAddFriend()
		{
			if (UIManager.TryGetTK("UIDialogInputBox", out UITKDialogInputBox tooltip))
			{
				tooltip.Open("Please type the name of the person you wish to add.", (s) =>
				{
					if (Authentication.IsAllowedCharacterName(s))
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
								else if (UIManager.TryGetTK("UIChat", out UITKChat chat))
								{
									chat.InstantiateChatMessage(ChatChannel.System, "", "You can't add yourself as a friend.");
								}
							}
							else if (UIManager.TryGetTK("UIChat", out UITKChat chat))
							{
								chat.InstantiateChatMessage(ChatChannel.System, "", "A person with that name could not be found.");
							}
						});
					}
				}, null);
			}
		}

		/// <summary>
		/// Returns the existing friend row for the given character, or creates and registers a new one.
		/// </summary>
		/// <param name="friendID">The friend's character ID.</param>
		/// <returns>The friend row.</returns>
		private FriendRow GetOrCreateRow(long friendID)
		{
			if (friends.TryGetValue(friendID, out FriendRow existing))
			{
				return existing;
			}

			VisualElement rowRoot = new VisualElement();
			rowRoot.AddToClassList(ROW_CLASS);

			Label name = new Label();
			name.AddToClassList(ROW_NAME_CLASS);
			rowRoot.Add(name);

			Label status = new Label();
			status.AddToClassList(ROW_STATUS_CLASS);
			rowRoot.Add(status);

			Button remove = new Button(() => OnButtonRemoveFriend(friendID))
			{
				text = "X",
			};
			remove.AddToClassList("fish-close-btn");
			remove.AddToClassList(ROW_REMOVE_CLASS);
			rowRoot.Add(remove);

			FriendRow row = new FriendRow
			{
				Root = rowRoot,
				Name = name,
				Status = status,
				FriendID = friendID,
			};

			friendList.Add(rowRoot);
			friends.Add(friendID, row);
			return row;
		}

		/// <summary>
		/// Removes all friend rows.
		/// </summary>
		private void ClearAll()
		{
			foreach (FriendRow row in friends.Values)
			{
				row.Root?.RemoveFromHierarchy();
			}
			friends.Clear();
		}
	}
}
