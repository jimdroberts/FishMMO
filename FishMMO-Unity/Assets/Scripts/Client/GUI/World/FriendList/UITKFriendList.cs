using System.Collections.Generic;
using UnityEngine.UIElements;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// Friend list panel. Renders each friend as a row carrying a presence dot, name, status and a
	/// remove button, and handles the add and remove actions. Rows are built at runtime rather than
	/// cloned from a per-entry control, and the shared dialog panels are reused for the prompts.
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
		/// <summary>USS class for a row's presence dot.</summary>
		private const string ROW_DOT_CLASS = "friend-row__dot";
		/// <summary>Name of the header label describing list state.</summary>
		private const string SUBTITLE_NAME = "friend-subtitle";
		/// <summary>Name of the header badge showing the online count.</summary>
		private const string COUNT_NAME = "friend-online-count";
		/// <summary>Name of the label shown when the list is empty.</summary>
		private const string EMPTY_NAME = "friend-empty";

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
			/// <summary>Presence dot leading the row.</summary>
			public VisualElement Dot;
			/// <summary>Last known online state, so the header count can be recomputed.</summary>
			public bool Online;
			/// <summary>The friend's character ID.</summary>
			public long FriendID;
		}

		/// <summary>All created friend rows keyed by character ID.</summary>
		private readonly Dictionary<long, FriendRow> friends = new Dictionary<long, FriendRow>();
		/// <summary>The container element that holds the generated friend rows.</summary>
		private VisualElement friendList;
		/// <summary>Header label describing list state.</summary>
		private Label subtitleLabel;
		/// <summary>Header badge showing how many friends are online.</summary>
		private Label onlineCountLabel;
		/// <summary>Label shown in place of the list when it is empty.</summary>
		private Label emptyLabel;

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
			subtitleLabel = root.Q<Label>(SUBTITLE_NAME);
			onlineCountLabel = root.Q<Label>(COUNT_NAME);
			emptyLabel = root.Q<Label>(EMPTY_NAME);
			RefreshHeader();

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

			row.Online = online;
			row.Status.text = online ? "Online" : "Offline";
			row.Status.EnableInClassList("friend-row__status--online", online);
			if (row.Dot != null)
			{
				row.Dot.EnableInClassList("fish-dot--online", online);
				row.Dot.EnableInClassList("fish-dot--offline", !online);
			}
			// Offline friends recede so the online ones read first.
			row.Root.EnableInClassList("fish-row--dim", !online);
			RefreshHeader();
		}

		/// <summary>
		/// Updates the header count, state line and empty placeholder from the list.
		/// </summary>
		/// <remarks>
		/// The badge counts friends who are online rather than friends in total, because that is
		/// the number a player opens this panel to find. The total is in the line beneath it.
		/// </remarks>
		private void RefreshHeader()
		{
			int total = friends.Count;
			int online = 0;
			foreach (KeyValuePair<long, FriendRow> kvp in friends)
			{
				if (kvp.Value.Online)
				{
					++online;
				}
			}

			if (onlineCountLabel != null)
			{
				onlineCountLabel.text = online.ToString();
				onlineCountLabel.EnableInClassList("fish-badge--good", online > 0);
			}

			if (subtitleLabel != null)
			{
				subtitleLabel.text = total == 0
					? "No friends added"
					: $"{online} of {total} online";
			}

			if (emptyLabel != null)
			{
				emptyLabel.style.display = total == 0 ? DisplayStyle.Flex : DisplayStyle.None;
			}
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
				RefreshHeader();
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
			/* The theme class supplies the hover state and leading accent rail shared by every
			 * roster; the panel class only carries geometry. */
			rowRoot.AddToClassList("fish-row");
			rowRoot.AddToClassList(ROW_CLASS);

			/* The presence dot leads the row. Colour is the fastest read for online state, and
			 * the status word beside it carries the same fact for anyone who cannot separate
			 * the dot colours. */
			VisualElement dot = new VisualElement();
			dot.AddToClassList("fish-dot");
			dot.AddToClassList(ROW_DOT_CLASS);
			rowRoot.Add(dot);

			Label name = new Label();
			name.AddToClassList("fish-row__name");
			name.AddToClassList(ROW_NAME_CLASS);
			rowRoot.Add(name);

			Label status = new Label();
			status.AddToClassList("fish-row__meta");
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
				Dot = dot,
				FriendID = friendID,
			};

			friendList.Add(rowRoot);
			friends.Add(friendID, row);
			RefreshHeader();
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
			RefreshHeader();
		}
	}
}
