using System;
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
	/// <remarks>
	/// The list is split into a MODEL (<see cref="friends"/>, plain data, owned by the character)
	/// and a VIEW (<see cref="rows"/>, elements owned by one visual tree). <c>UIDocument</c>
	/// re-clones the UXML on every enable, so the view is destroyed on each hide/show; keeping
	/// only the view left the panel permanently empty after the first close, because the data that
	/// would have redrawn it was inside the discarded elements. See <see cref="OnAfterStarting"/>.
	/// </remarks>
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
		/// One friend's data. Holds no <c>VisualElement</c> — see the class remarks.
		/// </summary>
		private sealed class FriendModel
		{
			/// <summary>The friend's character ID. The ONLY identity any action may use.</summary>
			public long FriendID;
			/// <summary>Whether the friend is online.</summary>
			public bool Online;
			/// <summary>
			/// Last resolved character name, cached so a rebuilt row renders without flashing
			/// blank. Display only — nothing resolves a target from it.
			/// </summary>
			public string Name = string.Empty;
		}

		/// <summary>
		/// Visual elements backing a single friend row.
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
		}

		/// <summary>The friend list MODEL, keyed by character ID. Survives tree rebuilds.</summary>
		private readonly Dictionary<long, FriendModel> friends = new Dictionary<long, FriendModel>();

		/// <summary>The friend list VIEW, keyed by character ID. Belongs to one visual tree.</summary>
		private readonly Dictionary<long, FriendRow> rows = new Dictionary<long, FriendRow>();

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
		/// <remarks>
		/// Runs against a fresh tree every time, so it drops the old view first — those elements
		/// belong to a tree that no longer exists.
		/// </remarks>
		public override void OnStarting()
		{
			rows.Clear();

			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			friendList = root.Q(FRIEND_LIST_NAME);
			subtitleLabel = root.Q<Label>(SUBTITLE_NAME);
			onlineCountLabel = root.Q<Label>(COUNT_NAME);
			emptyLabel = root.Q<Label>(EMPTY_NAME);

			Button add = root.Q<Button>(ADD_BUTTON_NAME);
			if (add != null)
			{
				add.clicked += OnButtonAddFriend;
			}
		}

		/// <summary>
		/// Re-applies the friend list after the visual tree was rebuilt.
		/// </summary>
		/// <remarks>
		/// The base implementation re-runs the character pre/post pair, which is what
		/// re-subscribes this panel to the friend controller; the rebuild below then redraws
		/// whatever the model already holds.
		/// </remarks>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();

			RebuildListView();
		}

		/// <summary>
		/// Clears all friend rows when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			ClearAll();

			base.OnDestroying();
		}

		/// <summary>
		/// Unsubscribes from the outgoing character's friend controller.
		/// </summary>
		/// <remarks>
		/// <c>UITKCharacterControl.OnAfterStarting</c> calls Pre then Post on every tree rebuild
		/// so the pair cancels out. Leaving this un-overridden made the Pre call a no-op, so every
		/// reopen stacked another subscription onto the same controller.
		/// </remarks>
		public override void OnPreSetCharacter()
		{
			base.OnPreSetCharacter();

			UnsubscribeFriendEvents();
		}

		/// <summary>
		/// Subscribes to friend controller events after the character is set.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character != null && Character.TryGet(out IFriendController friendController))
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

			UnsubscribeFriendEvents();
		}

		/// <summary>
		/// Drops the friend list once the character is gone.
		/// </summary>
		/// <remarks>
		/// The model outlives the visual tree on purpose, so nothing else would clear it — and a
		/// character with no friends generates no list traffic to overwrite it, which is what left
		/// the previous character's friends on screen after a character switch.
		/// </remarks>
		public override void OnPostUnsetCharacter()
		{
			base.OnPostUnsetCharacter();

			ClearAll();
		}

		/// <summary>
		/// Removes this panel's handlers from the current character's friend controller.
		/// </summary>
		private void UnsubscribeFriendEvents()
		{
			if (Character == null || !Character.TryGet(out IFriendController friendController))
			{
				return;
			}

			friendController.OnAddFriend -= FriendController_OnAddFriend;
			friendController.OnRemoveFriend -= FriendController_OnRemoveFriend;
		}

		/// <summary>
		/// Adds or updates a friend with the latest name and online status.
		/// </summary>
		/// <param name="friendID">The friend's character ID.</param>
		/// <param name="online">Whether the friend is online.</param>
		public void FriendController_OnAddFriend(long friendID, bool online)
		{
			if (!friends.TryGetValue(friendID, out FriendModel model))
			{
				model = new FriendModel
				{
					FriendID = friendID,
				};
				friends.Add(friendID, model);
			}

			model.Online = online;

			/* The name lookup may complete after the tree has been replaced, so the callback
			 * writes into the MODEL and re-reads the view rather than closing over an element. */
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, friendID, (n) =>
			{
				if (friends.TryGetValue(friendID, out FriendModel target))
				{
					target.Name = n;
					ApplyModelToRow(target);
				}
			});

			GetOrCreateRow(model);
			ApplyModelToRow(model);
			RefreshHeader();
		}

		/// <summary>
		/// Removes a friend.
		/// </summary>
		/// <param name="friendID">The friend's character ID.</param>
		public void FriendController_OnRemoveFriend(long friendID)
		{
			bool removed = friends.Remove(friendID);

			if (rows.TryGetValue(friendID, out FriendRow row))
			{
				row.Root?.RemoveFromHierarchy();
				rows.Remove(friendID);
			}

			if (removed)
			{
				RefreshHeader();
			}
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
			foreach (KeyValuePair<long, FriendModel> kvp in friends)
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
								if (Character != null && Character.ID != id)
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
		/// Returns the existing row for a friend, or builds one into the current tree.
		/// </summary>
		/// <param name="model">The friend the row renders.</param>
		/// <returns>The friend row, or null when there is no tree to build into.</returns>
		private FriendRow GetOrCreateRow(FriendModel model)
		{
			if (rows.TryGetValue(model.FriendID, out FriendRow existing))
			{
				return existing;
			}

			if (friendList == null)
			{
				/* No tree yet — the panel has never been shown. The model still holds the friend,
				 * and OnAfterStarting builds the row as soon as there is somewhere to put it. */
				return null;
			}

			long friendID = model.FriendID;

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
			};

			// Captured by ID, never by element — the elements are replaced on every rebuild.
			rowRoot.RegisterCallback<PointerDownEvent>(evt => OnRowPointerDown(evt, friendID));

			friendList.Add(rowRoot);
			rows.Add(friendID, row);
			return row;
		}

		/// <summary>
		/// Writes one friend's model values into its row, if the row currently exists.
		/// </summary>
		/// <param name="model">The friend to render.</param>
		private void ApplyModelToRow(FriendModel model)
		{
			if (!rows.TryGetValue(model.FriendID, out FriendRow row))
			{
				return;
			}

			row.Name.text = model.Name;
			row.Status.text = model.Online ? "Online" : "Offline";
			row.Status.EnableInClassList("friend-row__status--online", model.Online);

			if (row.Dot != null)
			{
				row.Dot.EnableInClassList("fish-dot--online", model.Online);
				row.Dot.EnableInClassList("fish-dot--offline", !model.Online);
			}

			// Offline friends recede so the online ones read first.
			row.Root.EnableInClassList("fish-row--dim", !model.Online);
		}

		/// <summary>
		/// Rebuilds every row from the model into the current visual tree.
		/// </summary>
		private void RebuildListView()
		{
			rows.Clear();

			if (friendList != null)
			{
				friendList.Clear();
			}

			foreach (FriendModel model in friends.Values)
			{
				GetOrCreateRow(model);
				ApplyModelToRow(model);
			}

			RefreshHeader();
		}

		/// <summary>
		/// Drops both the model and the view.
		/// </summary>
		private void ClearAll()
		{
			foreach (FriendRow row in rows.Values)
			{
				row.Root?.RemoveFromHierarchy();
			}
			rows.Clear();
			friends.Clear();
			RefreshHeader();
		}

		/// <summary>
		/// Opens the per-friend context menu on right-click.
		/// </summary>
		/// <param name="evt">The pointer-down event.</param>
		/// <param name="friendID">The friend's character ID.</param>
		private void OnRowPointerDown(PointerDownEvent evt, long friendID)
		{
			// 1 is the right button. Left-click is left free for selection.
			if (evt.button != 1)
			{
				return;
			}

			evt.StopPropagation();

			OpenFriendContextMenu(friendID);
		}

		/// <summary>
		/// Builds and opens the context menu for one friend.
		/// </summary>
		/// <param name="friendID">The friend's character ID.</param>
		/// <remarks>
		/// Every entry closes over <paramref name="friendID"/>, never over the row's name label.
		/// </remarks>
		private void OpenFriendContextMenu(long friendID)
		{
			if (!UIManager.TryGetTK("UIContextMenu", out UITKContextMenu contextMenu) ||
				!friends.TryGetValue(friendID, out FriendModel model))
			{
				return;
			}

			List<(string label, Action callback)> entries = new List<(string, Action)>();

			string displayName = model.Name;
			if (!string.IsNullOrEmpty(displayName))
			{
				entries.Add(("Message", () =>
				{
					if (UIManager.TryGetTK("UIChat", out UITKChat uiChat))
					{
						uiChat.SetInputText($"/tell {displayName} ");
					}
				}
				));
			}

			entries.Add(("Invite to Party", () =>
			{
				Client.Broadcast(new PartyInviteBroadcast()
				{
					TargetCharacterID = friendID,
				}, Channel.Reliable);
			}
			));

			entries.Add(("Invite to Guild", () =>
			{
				Client.Broadcast(new GuildInviteBroadcast()
				{
					TargetCharacterID = friendID,
				}, Channel.Reliable);
			}
			));

			entries.Add(("Remove Friend", () => OnButtonRemoveFriend(friendID)));

			contextMenu.Open(entries);
		}
	}
}
