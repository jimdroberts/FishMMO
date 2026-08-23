using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit party panel. Renders party members as dynamic rows, handles create/leave/invite
	/// actions, and shows a per-member context menu (message / add friend / promote / kick).
	/// The shared dialog and context-menu overlays are resolved by name through the
	/// <see cref="UIManager"/> rather than referenced directly.
	/// </summary>
	/// <remarks>
	/// The roster is split into a MODEL (<see cref="roster"/>, plain data, owned by the character)
	/// and a VIEW (<see cref="rows"/>, elements owned by one visual tree). <c>UIDocument</c>
	/// re-clones the UXML on every enable, so the view is destroyed on each hide/show. Keeping
	/// only the view — as this panel used to — meant the party vanished permanently after the
	/// first close, because the data that would have redrawn it was inside the discarded elements.
	/// See <see cref="OnAfterStarting"/>.
	/// </remarks>
	public class UITKParty : UITKCharacterControl
	{
		/// <summary>Name of the container that holds the generated member rows.</summary>
		private const string MEMBER_LIST_NAME = "party-member-list";

		/// <summary>Name of the create-party button.</summary>
		private const string CREATE_BUTTON_NAME = "party-create";

		/// <summary>Name of the leave-party button.</summary>
		private const string LEAVE_BUTTON_NAME = "party-leave";

		/// <summary>Name of the invite-to-party button.</summary>
		private const string INVITE_BUTTON_NAME = "party-invite";

		/// <summary>USS class applied to each generated member row.</summary>
		private const string ROW_CLASS = "party-member";

		/// <summary>USS class applied to a member row's name label.</summary>
		private const string ROW_NAME_CLASS = "party-member__name";

		/// <summary>USS class applied to a member row's rank label.</summary>
		private const string ROW_RANK_CLASS = "party-member__rank";

		/// <summary>USS class applied to a member row's health track.</summary>
		private const string ROW_HEALTH_CLASS = "party-member__health";

		/// <summary>USS class applied to a member row's health fill.</summary>
		private const string ROW_HEALTH_FILL_CLASS = "party-member__health-fill";
		/// <summary>Name of the label showing party state in the header.</summary>
		private const string SUBTITLE_NAME = "party-subtitle";
		/// <summary>Name of the badge showing member count.</summary>
		private const string COUNT_NAME = "party-count";
		/// <summary>Name of the label shown when the player is not in a party.</summary>
		private const string EMPTY_NAME = "party-empty";
		/// <summary>Name of the column caption strip.</summary>
		private const string COLUMNS_NAME = "party-columns";
		/// <summary>
		/// Party size shown in the header badge.
		/// </summary>
		/// <remarks>
		/// Display only. The server owns the real cap; this is the denominator a player reads,
		/// and showing a count with no ceiling tells them nothing about how full the party is.
		/// </remarks>
		private const int MAX_PARTY_DISPLAY = 6;

		/// <summary>
		/// One party member's data. Holds no <c>VisualElement</c> — see the class remarks.
		/// </summary>
		private sealed class MemberModel
		{
			/// <summary>The member's character ID. The ONLY identity any action may use.</summary>
			public long CharacterID;
			/// <summary>The member's party rank.</summary>
			public PartyRank Rank;
			/// <summary>The member's health fraction, 0-1.</summary>
			public float HealthPCT;
			/// <summary>
			/// Last resolved character name, cached so a rebuilt row renders without flashing
			/// blank. Display only — nothing resolves a target from it.
			/// </summary>
			public string Name = string.Empty;
		}

		/// <summary>
		/// Visual elements backing a single party member row.
		/// </summary>
		private sealed class MemberRow
		{
			/// <summary>Root container for the row.</summary>
			public VisualElement Root;
			/// <summary>Member name label.</summary>
			public Label Name;
			/// <summary>Member rank marker label.</summary>
			public Label Rank;
			/// <summary>Member health fill element.</summary>
			public VisualElement HealthFill;
		}

		/// <summary>The roster MODEL, keyed by character ID. Survives tree rebuilds.</summary>
		private readonly Dictionary<long, MemberModel> roster = new Dictionary<long, MemberModel>();

		/// <summary>The roster VIEW, keyed by character ID. Belongs to one visual tree.</summary>
		private readonly Dictionary<long, MemberRow> rows = new Dictionary<long, MemberRow>();

		/// <summary>The container element that holds the generated member rows.</summary>
		private VisualElement memberList;
		/// <summary>Header label describing the current party state.</summary>
		private Label subtitleLabel;
		/// <summary>Header badge showing the member count.</summary>
		private Label countLabel;
		/// <summary>Label shown in place of the roster when there is no party.</summary>
		private Label emptyLabel;
		/// <summary>Column caption strip, hidden while the roster is empty.</summary>
		private VisualElement columns;
		/// <summary>Create-party button.</summary>
		private Button createButton;
		/// <summary>Invite-to-party button.</summary>
		private Button inviteButton;
		/// <summary>Leave-party button.</summary>
		private Button leaveButton;

		/// <summary>
		/// Queries the member list and wires up the action buttons.
		/// </summary>
		/// <remarks>
		/// Runs against a fresh tree every time, so it drops the old view first — those elements
		/// belong to a tree that no longer exists. The buttons are new objects, so <c>+=</c>
		/// cannot accumulate handlers across rebuilds.
		/// </remarks>
		public override void OnStarting()
		{
			rows.Clear();

			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			memberList = root.Q(MEMBER_LIST_NAME);
			subtitleLabel = root.Q<Label>(SUBTITLE_NAME);
			countLabel = root.Q<Label>(COUNT_NAME);
			emptyLabel = root.Q<Label>(EMPTY_NAME);
			columns = root.Q(COLUMNS_NAME);

			createButton = root.Q<Button>(CREATE_BUTTON_NAME);
			if (createButton != null)
			{
				createButton.clicked += OnButtonCreateParty;
			}

			leaveButton = root.Q<Button>(LEAVE_BUTTON_NAME);
			if (leaveButton != null)
			{
				leaveButton.clicked += OnButtonLeaveParty;
			}

			inviteButton = root.Q<Button>(INVITE_BUTTON_NAME);
			if (inviteButton != null)
			{
				inviteButton.clicked += OnButtonInviteToParty;
			}
		}

		/// <summary>
		/// Re-applies the roster after the visual tree was rebuilt.
		/// </summary>
		/// <remarks>
		/// The base implementation re-runs the character pre/post pair, which is what
		/// re-subscribes this panel to the party controller; the rebuild below then redraws
		/// whatever the model already holds.
		/// </remarks>
		protected override void OnAfterStarting()
		{
			base.OnAfterStarting();

			RebuildRosterView();
		}

		/// <summary>
		/// Clears all member rows when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			ClearRoster();

			base.OnDestroying();
		}

		/// <summary>
		/// Unsubscribes from the outgoing character's party controller.
		/// </summary>
		/// <remarks>
		/// <c>UITKCharacterControl.OnAfterStarting</c> calls Pre then Post on every tree rebuild
		/// so the pair cancels out. Leaving this un-overridden made the Pre call a no-op, so
		/// every reopen stacked another subscription onto the same controller.
		/// </remarks>
		public override void OnPreSetCharacter()
		{
			base.OnPreSetCharacter();

			UnsubscribePartyEvents();
		}

		/// <summary>
		/// Subscribes to party controller events after the character is set.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character != null && Character.TryGet(out IPartyController partyController))
			{
				partyController.OnPartyCreated += OnPartyCreated;
				partyController.OnReceivePartyInvite += PartyController_OnReceivePartyInvite;
				partyController.OnAddPartyMember += OnPartyAddMember;
				partyController.OnUpdatePartyMemberHealth += OnPartyUpdateMemberHealth;
				partyController.OnValidatePartyMembers += PartyController_OnValidatePartyMembers;
				partyController.OnRemovePartyMember += OnPartyRemoveMember;
				partyController.OnLeaveParty += OnLeaveParty;
			}
		}

		/// <summary>
		/// Unsubscribes from party controller events before the character is unset.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			UnsubscribePartyEvents();
		}

		/// <summary>
		/// Drops the roster once the character is gone.
		/// </summary>
		/// <remarks>
		/// The model outlives the visual tree on purpose, so nothing else would clear it — and a
		/// partyless character generates no roster traffic to overwrite it, which is what left the
		/// previous character's party on screen after a character switch.
		/// </remarks>
		public override void OnPostUnsetCharacter()
		{
			base.OnPostUnsetCharacter();

			ClearRoster();
		}

		/// <summary>
		/// Removes this panel's handlers from the current character's party controller.
		/// </summary>
		private void UnsubscribePartyEvents()
		{
			if (Character == null || !Character.TryGet(out IPartyController partyController))
			{
				return;
			}

			partyController.OnPartyCreated -= OnPartyCreated;
			partyController.OnReceivePartyInvite -= PartyController_OnReceivePartyInvite;
			partyController.OnAddPartyMember -= OnPartyAddMember;
			partyController.OnUpdatePartyMemberHealth -= OnPartyUpdateMemberHealth;
			partyController.OnValidatePartyMembers -= PartyController_OnValidatePartyMembers;
			partyController.OnRemovePartyMember -= OnPartyRemoveMember;
			partyController.OnLeaveParty -= OnLeaveParty;
		}

		/// <summary>
		/// Prompts the local player to accept or decline a received party invite.
		/// </summary>
		/// <param name="inviterCharacterID">The inviter's character ID.</param>
		/// <remarks>
		/// The inviter's ID travels back on the answer so the server can confirm the player is
		/// answering the invitation they were actually shown. See
		/// <see cref="PartyAcceptInviteBroadcast"/>.
		/// </remarks>
		public void PartyController_OnReceivePartyInvite(long inviterCharacterID)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, inviterCharacterID, (n) =>
			{
				if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox uiTooltip))
				{
					uiTooltip.Open("You have been invited to join " + n + "'s party. Would you like to join?",
					() =>
					{
						Client.Broadcast(new PartyAcceptInviteBroadcast()
						{
							InviterCharacterID = inviterCharacterID,
						}, Channel.Reliable);
					},
					() =>
					{
						Client.Broadcast(new PartyDeclineInviteBroadcast()
						{
							InviterCharacterID = inviterCharacterID,
						}, Channel.Reliable);
					});
				}
			});
		}

		/// <summary>
		/// Removes member rows that are no longer part of the validated member set.
		/// </summary>
		/// <param name="newMembers">The set of valid member IDs.</param>
		public void PartyController_OnValidatePartyMembers(HashSet<long> newMembers)
		{
			foreach (long id in new List<long>(roster.Keys))
			{
				if (!newMembers.Contains(id))
				{
					OnPartyRemoveMember(id);
				}
			}
		}

		/// <summary>
		/// Adds the local player as a member row when a party is created.
		/// </summary>
		/// <param name="location">Location string (unused).</param>
		public void OnPartyCreated(string location)
		{
			if (Character == null ||
				!Character.TryGet(out IPartyController partyController))
			{
				return;
			}

			MemberModel model = GetOrCreateModel(Character.ID);
			model.Name = Character.CharacterName;
			model.Rank = partyController.Rank;
			model.HealthPCT = Character.TryGet(out ICharacterAttributeController attributeController)
				? attributeController.GetHealthResourceAttributeCurrentPercentage()
				: 0.0f;

			GetOrCreateRow(model);
			ApplyModelToRow(model);
			RefreshHeader();
		}

		/// <summary>
		/// Clears all member rows when leaving the party.
		/// </summary>
		public void OnLeaveParty()
		{
			ClearRoster();
		}

		/// <summary>
		/// Adds or updates a member.
		/// </summary>
		/// <param name="characterID">The member's character ID.</param>
		/// <param name="rank">The member's party rank.</param>
		/// <param name="healthPCT">The member's health percentage (0-1).</param>
		public void OnPartyAddMember(long characterID, PartyRank rank, float healthPCT)
		{
			MemberModel model = GetOrCreateModel(characterID);
			model.Rank = rank;
			model.HealthPCT = healthPCT;

			/* The name lookup may complete after the tree has been replaced, so the callback
			 * writes into the MODEL and re-reads the view rather than closing over an element. */
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, characterID, (n) =>
			{
				if (roster.TryGetValue(characterID, out MemberModel target))
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
		/// Applies a live health update for one party member.
		/// </summary>
		/// <param name="characterID">The member's character ID.</param>
		/// <param name="healthPCT">The member's health fraction, 0-1.</param>
		/// <remarks>
		/// Health used to arrive ONLY inside the roster payload, which the scene server rebuilds
		/// from the guild/party database rows — and those rows are written on connect and on
		/// disconnect and at no other time. Every bar therefore showed the value the member logged
		/// in with, for the whole session. The party system now pushes live vitals for the members
		/// it can see on its own scene server; this is the handler for that.
		/// </remarks>
		public void OnPartyUpdateMemberHealth(long characterID, float healthPCT)
		{
			if (!roster.TryGetValue(characterID, out MemberModel model))
			{
				return;
			}

			model.HealthPCT = healthPCT;
			ApplyModelToRow(model);
		}

		/// <summary>
		/// Removes a member.
		/// </summary>
		/// <param name="characterID">The member's character ID.</param>
		public void OnPartyRemoveMember(long characterID)
		{
			bool removed = roster.Remove(characterID);

			if (rows.TryGetValue(characterID, out MemberRow row))
			{
				row.Root?.RemoveFromHierarchy();
				rows.Remove(characterID);
			}

			if (removed)
			{
				RefreshHeader();
			}
		}

		/// <summary>
		/// Broadcasts a create-party request when not already in a party.
		/// </summary>
		public void OnButtonCreateParty()
		{
			if (Character != null &&
				Character.TryGet(out IPartyController partyController) &&
				partyController.ID < 1)
			{
				Client.Broadcast(new PartyCreateBroadcast(), Channel.Reliable);
			}
		}

		/// <summary>
		/// Confirms then broadcasts a leave-party request.
		/// </summary>
		public void OnButtonLeaveParty()
		{
			if (Character != null &&
				Character.TryGet(out IPartyController partyController) &&
				partyController.ID > 0)
			{
				if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox tooltip))
				{
					tooltip.Open("Are you sure you want to leave your party?", () =>
					{
						Client.Broadcast(new PartyLeaveBroadcast(), Channel.Reliable);
					}, () => { });
				}
			}
		}

		/// <summary>
		/// Invites the current target, or prompts for a name, to the party.
		/// </summary>
		public void OnButtonInviteToParty()
		{
			if (Character != null &&
				Character.TryGet(out IPartyController partyController) &&
				partyController.ID > 0 &&
				Client.NetworkManager.IsClientStarted)
			{
				if (Character.TryGet(out ITargetController targetController) &&
					targetController.Current.Target != null)
				{
					IPlayerCharacter targetCharacter = targetController.Current.Target.GetComponent<IPlayerCharacter>();
					if (targetCharacter != null)
					{
						Client.Broadcast(new PartyInviteBroadcast()
						{
							TargetCharacterID = targetCharacter.ID,
						}, Channel.Reliable);

						return;
					}
				}

				if (UIManager.TryGetTK("UIDialogInputBox", out UITKDialogInputBox tooltip))
				{
					tooltip.Open("Please type the name of the person you wish to invite.", (s) =>
					{
						if (Authentication.IsAllowedCharacterName(s))
						{
							ClientNamingSystem.GetCharacterID(s, (id) =>
							{
								if (id != 0)
								{
									if (Character != null && Character.ID != id)
									{
										Client.Broadcast(new PartyInviteBroadcast()
										{
											TargetCharacterID = id,
										}, Channel.Reliable);
									}
									else if (UIManager.TryGetTK("UIChat", out UITKChat chat))
									{
										chat.InstantiateChatMessage(ChatChannel.System, "", "You can't invite yourself to the party.");
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
		}

		/// <summary>
		/// Returns the existing model for a member, or registers a new one.
		/// </summary>
		/// <param name="characterID">The member's character ID.</param>
		/// <returns>The member model.</returns>
		private MemberModel GetOrCreateModel(long characterID)
		{
			if (!roster.TryGetValue(characterID, out MemberModel model))
			{
				model = new MemberModel
				{
					CharacterID = characterID,
				};
				roster.Add(characterID, model);
			}
			return model;
		}

		/// <summary>
		/// Returns the existing row for a member, or builds one into the current tree.
		/// </summary>
		/// <param name="model">The member the row renders.</param>
		/// <returns>The member row, or null when there is no tree to build into.</returns>
		private MemberRow GetOrCreateRow(MemberModel model)
		{
			if (rows.TryGetValue(model.CharacterID, out MemberRow existing))
			{
				return existing;
			}

			if (memberList == null)
			{
				/* No tree yet — the panel has never been shown. The model still holds the member,
				 * and OnAfterStarting builds the row as soon as there is somewhere to put it. */
				return null;
			}

			VisualElement rowRoot = new VisualElement();
			/* The theme class supplies the hover state and the leading accent rail every
			 * roster in the game shares; the panel class only carries geometry. */
			rowRoot.AddToClassList("fish-row");
			rowRoot.AddToClassList(ROW_CLASS);

			Label name = new Label();
			name.AddToClassList("fish-row__name");
			name.AddToClassList(ROW_NAME_CLASS);
			rowRoot.Add(name);

			Label rank = new Label();
			rank.AddToClassList("fish-row__meta");
			rank.AddToClassList(ROW_RANK_CLASS);
			rowRoot.Add(rank);

			VisualElement health = new VisualElement();
			health.AddToClassList("fish-bar");
			health.AddToClassList(ROW_HEALTH_CLASS);
			VisualElement healthFill = new VisualElement();
			healthFill.AddToClassList("fish-bar__fill");
			healthFill.AddToClassList("fish-bar__fill--hp");
			healthFill.AddToClassList(ROW_HEALTH_FILL_CLASS);
			health.Add(healthFill);
			rowRoot.Add(health);

			MemberRow row = new MemberRow
			{
				Root = rowRoot,
				Name = name,
				Rank = rank,
				HealthFill = healthFill,
			};

			/* Captured by ID, never by element — a row's elements are replaced on every rebuild
			 * and the ID is the only identity that stays true across one. */
			long characterID = model.CharacterID;
			rowRoot.RegisterCallback<PointerDownEvent>(evt => OnMemberPointerDown(evt, characterID));

			memberList.Add(rowRoot);
			rows.Add(characterID, row);
			return row;
		}

		/// <summary>
		/// Writes one member's model values into its row, if the row currently exists.
		/// </summary>
		/// <param name="model">The member to render.</param>
		private void ApplyModelToRow(MemberModel model)
		{
			if (!rows.TryGetValue(model.CharacterID, out MemberRow row))
			{
				return;
			}

			row.Name.text = model.Name;
			row.Rank.text = model.Rank == PartyRank.Leader ? "*" : string.Empty;

			if (row.HealthFill != null)
			{
				row.HealthFill.style.width = Length.Percent(Mathf.Clamp01(model.HealthPCT) * 100.0f);
			}
		}

		/// <summary>
		/// Rebuilds every row from the model into the current visual tree.
		/// </summary>
		private void RebuildRosterView()
		{
			rows.Clear();

			if (memberList != null)
			{
				memberList.Clear();
			}

			foreach (MemberModel model in roster.Values)
			{
				GetOrCreateRow(model);
				ApplyModelToRow(model);
			}

			RefreshHeader();
		}

		/// <summary>
		/// Drops both the model and the view.
		/// </summary>
		private void ClearRoster()
		{
			foreach (MemberRow row in rows.Values)
			{
				row.Root?.RemoveFromHierarchy();
			}
			rows.Clear();
			roster.Clear();
			RefreshHeader();
		}

		/// <summary>
		/// Updates the header count, state line and empty placeholder from the roster.
		/// </summary>
		/// <remarks>
		/// Called after every add, remove and clear rather than on a timer: the roster is the
		/// only thing these three read, and a header that disagrees with the list under it is
		/// worse than no header at all.
		/// </remarks>
		private void RefreshHeader()
		{
			int count = roster.Count;
			bool inParty = count > 0;

			if (countLabel != null)
			{
				countLabel.text = $"{count}/{MAX_PARTY_DISPLAY}";
				countLabel.EnableInClassList("fish-badge--accent", inParty);
			}

			if (subtitleLabel != null)
			{
				subtitleLabel.text = inParty
					? (count == 1 ? "1 member" : $"{count} members")
					: "Not in a party";
			}

			if (emptyLabel != null)
			{
				emptyLabel.style.display = inParty ? DisplayStyle.None : DisplayStyle.Flex;
			}

			if (columns != null)
			{
				// Column captions over an empty list name fields that are not there.
				columns.style.display = inParty ? DisplayStyle.Flex : DisplayStyle.None;
			}

			/* Same argument as the columns above, applied to the footer. OnButtonCreateParty
			 * already refuses unless ID < 1, and Leave and Invite unless ID > 0, so showing all
			 * three at once offers two buttons that are guaranteed no-ops. Draw what membership
			 * actually permits instead. */
			if (createButton != null)
			{
				createButton.style.display = inParty ? DisplayStyle.None : DisplayStyle.Flex;
			}

			if (leaveButton != null)
			{
				leaveButton.style.display = inParty ? DisplayStyle.Flex : DisplayStyle.None;
			}

			/* Invite answers to rank as well as to membership, because the server does:
			 * PartySystem.OnServerPartyInviteBroadcastReceived drops the request unless the
			 * inviter is the Leader, and drops it *silently* — so a member offered this button
			 * gets a prompt, sends a broadcast and sees nothing happen, which is the exact class
			 * of dead control the rest of this block removes. The guild panel gates its own
			 * Invite on GuildPermissions.Invite for the same reason. Rank is kept current on
			 * create, on add (which is also how a promotion arrives) and on leave. */
			if (inviteButton != null)
			{
				bool canInvite = inParty &&
								 Character != null &&
								 Character.TryGet(out IPartyController inviteController) &&
								 inviteController.Rank == PartyRank.Leader;

				inviteButton.style.display = canInvite ? DisplayStyle.Flex : DisplayStyle.None;
			}
		}

		/// <summary>
		/// Opens the per-member context menu on right-click.
		/// </summary>
		/// <param name="evt">The pointer-down event.</param>
		/// <param name="characterID">The member's character ID.</param>
		/// <remarks>
		/// Right-click through the shared context menu, replacing the dropdown. The dropdown call
		/// hid the panel and then added its entries, but hiding disables the document — so every
		/// entry was added to a tree the following <c>Show()</c> discarded, and the menu could
		/// never appear with anything in it.
		/// </remarks>
		private void OnMemberPointerDown(PointerDownEvent evt, long characterID)
		{
			// 1 is the right button. Left-click is left free for selection.
			if (evt.button != 1)
			{
				return;
			}

			evt.StopPropagation();

			OpenMemberContextMenu(characterID);
		}

		/// <summary>
		/// Builds and opens the context menu for one party member.
		/// </summary>
		/// <param name="characterID">The member's character ID.</param>
		/// <remarks>
		/// Every entry closes over <paramref name="characterID"/>. The previous version read the
		/// target back out of the row's name LABEL and pushed it through an asynchronous name
		/// lookup to recover an ID it already had — so a roster that changed between the click and
		/// the reply could kick a different member than the one clicked.
		/// </remarks>
		private void OpenMemberContextMenu(long characterID)
		{
			if (Character == null ||
				Character.ID == characterID ||
				!UIManager.TryGetTK("UIContextMenu", out UITKContextMenu contextMenu) ||
				!Character.TryGet(out IPartyController partyController) ||
				partyController.ID < 1 ||
				!roster.TryGetValue(characterID, out MemberModel model))
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

			entries.Add(("Add Friend", () =>
			{
				Client.Broadcast(new FriendAddNewBroadcast()
				{
					CharacterID = characterID,
				}, Channel.Reliable);
			}
			));

			/* Drawing decisions only. The server re-derives both ranks from its own state before
			 * it acts, so an entry a client should not have offered is refused, not obeyed. */
			if (model.Rank < partyController.Rank)
			{
				entries.Add(("Promote to Leader", () =>
				{
					Client.Broadcast(new PartyChangeRankBroadcast()
					{
						CharacterID = characterID,
						Rank = PartyRank.Leader,
					}, Channel.Reliable);
				}
				));

				entries.Add(("Kick", () =>
				{
					if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox dialog))
					{
						dialog.Open($"Remove {displayName} from the party?", () =>
						{
							Client.Broadcast(new PartyRemoveBroadcast()
							{
								CharacterID = characterID,
							}, Channel.Reliable);
						}, () => { });
					}
				}
				));
			}

			contextMenu.Open(entries);
		}
	}
}
