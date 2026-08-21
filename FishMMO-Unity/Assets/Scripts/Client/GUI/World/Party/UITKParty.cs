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
	/// actions, and shows a per-member context dropdown (message / add friend / promote / kick).
	/// The shared dialog and dropdown overlays are resolved by name through the
	/// <see cref="UIManager"/> rather than referenced directly.
	/// </summary>
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
		/// Visual elements and state backing a single party member row.
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
			/// <summary>The member's character ID.</summary>
			public long CharacterID;
			/// <summary>The member's rank name (used for promote/kick gating).</summary>
			public string RankName = string.Empty;
		}

		/// <summary>All created party member rows keyed by character ID.</summary>
		private readonly Dictionary<long, MemberRow> members = new Dictionary<long, MemberRow>();
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

		/// <summary>
		/// Queries the member list and wires up the action buttons.
		/// </summary>
		public override void OnStarting()
		{
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

			RefreshHeader();

			Button create = root.Q<Button>(CREATE_BUTTON_NAME);
			if (create != null)
			{
				create.clicked += OnButtonCreateParty;
			}

			Button leave = root.Q<Button>(LEAVE_BUTTON_NAME);
			if (leave != null)
			{
				leave.clicked += OnButtonLeaveParty;
			}

			Button invite = root.Q<Button>(INVITE_BUTTON_NAME);
			if (invite != null)
			{
				invite.clicked += OnButtonInviteToParty;
			}
		}

		/// <summary>
		/// Clears all member rows when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			ClearAllMembers();
		}

		/// <summary>
		/// Subscribes to party controller events after the character is set.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character.TryGet(out IPartyController partyController))
			{
				partyController.OnPartyCreated += OnPartyCreated;
				partyController.OnReceivePartyInvite += PartyController_OnReceivePartyInvite;
				partyController.OnAddPartyMember += OnPartyAddMember;
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

			if (Character.TryGet(out IPartyController partyController))
			{
				partyController.OnPartyCreated -= OnPartyCreated;
				partyController.OnReceivePartyInvite -= PartyController_OnReceivePartyInvite;
				partyController.OnAddPartyMember -= OnPartyAddMember;
				partyController.OnValidatePartyMembers -= PartyController_OnValidatePartyMembers;
				partyController.OnRemovePartyMember -= OnPartyRemoveMember;
				partyController.OnLeaveParty -= OnLeaveParty;
			}
		}

		/// <summary>
		/// Prompts the local player to accept or decline a received party invite.
		/// </summary>
		/// <param name="inviterCharacterID">The inviter's character ID.</param>
		public void PartyController_OnReceivePartyInvite(long inviterCharacterID)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, inviterCharacterID, (n) =>
			{
				if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox uiTooltip))
				{
					uiTooltip.Open("You have been invited to join " + n + "'s party. Would you like to join?",
					() =>
					{
						Client.Broadcast(new PartyAcceptInviteBroadcast(), Channel.Reliable);
					},
					() =>
					{
						Client.Broadcast(new PartyDeclineInviteBroadcast(), Channel.Reliable);
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
			foreach (long id in new HashSet<long>(members.Keys))
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
			if (Character != null &&
				memberList != null &&
				Character.TryGet(out IPartyController partyController))
			{
				MemberRow row = GetOrCreateRow(Character.ID);
				row.Name.text = Character.CharacterName;
				row.RankName = partyController.Rank.ToString();
				row.Rank.text = partyController.Rank == PartyRank.Leader ? "*" : string.Empty;
				SetRowHealth(row, Character.TryGet(out ICharacterAttributeController attributeController)
					? attributeController.GetHealthResourceAttributeCurrentPercentage()
					: 0.0f);
			}
		}

		/// <summary>
		/// Clears all member rows when leaving the party.
		/// </summary>
		public void OnLeaveParty()
		{
			ClearAllMembers();
		}

		/// <summary>
		/// Adds or updates a member row.
		/// </summary>
		/// <param name="characterID">The member's character ID.</param>
		/// <param name="rank">The member's party rank.</param>
		/// <param name="healthPCT">The member's health percentage (0-1).</param>
		public void OnPartyAddMember(long characterID, PartyRank rank, float healthPCT)
		{
			if (memberList == null)
			{
				return;
			}

			MemberRow row = GetOrCreateRow(characterID);

			ClientNamingSystem.SetName(NamingSystemType.CharacterName, characterID, (n) =>
			{
				row.Name.text = n;
			});

			row.RankName = rank.ToString();
			row.Rank.text = rank == PartyRank.Leader ? "*" : string.Empty;
			SetRowHealth(row, healthPCT);
		}

		/// <summary>
		/// Removes a member row.
		/// </summary>
		/// <param name="characterID">The member's character ID.</param>
		public void OnPartyRemoveMember(long characterID)
		{
			if (members.TryGetValue(characterID, out MemberRow row))
			{
				row.Root?.RemoveFromHierarchy();
				members.Remove(characterID);
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
									if (Character.ID != id)
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
		/// Returns the existing member row for the given character, or creates and registers a new one.
		/// </summary>
		/// <param name="characterID">The member's character ID.</param>
		/// <returns>The member row.</returns>
		private MemberRow GetOrCreateRow(long characterID)
		{
			if (members.TryGetValue(characterID, out MemberRow existing))
			{
				return existing;
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
				CharacterID = characterID,
			};

			rowRoot.RegisterCallback<PointerDownEvent>(evt => OnMemberPointerDown(evt, row));

			memberList.Add(rowRoot);
			members.Add(characterID, row);
			RefreshHeader();
			return row;
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
			int count = members.Count;
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
		}

		/// <summary>
		/// Sets a member row's health fill width as a 0-1 fraction.
		/// </summary>
		/// <param name="row">The member row.</param>
		/// <param name="fraction">The health fraction.</param>
		private void SetRowHealth(MemberRow row, float fraction)
		{
			if (row.HealthFill != null)
			{
				row.HealthFill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100.0f);
			}
		}

		/// <summary>
		/// Opens the per-member context dropdown on left-click.
		/// </summary>
		/// <param name="evt">The pointer-down event.</param>
		/// <param name="row">The member row that was clicked.</param>
		private void OnMemberPointerDown(PointerDownEvent evt, MemberRow row)
		{
			if (evt.button != 0)
			{
				return;
			}

			ShowMemberDropdown(row);
		}

		/// <summary>
		/// Builds and shows the context dropdown for a party member (message / add friend / promote / kick).
		/// </summary>
		/// <param name="row">The member row.</param>
		private void ShowMemberDropdown(MemberRow row)
		{
			if (!UIManager.TryGetTK("UIDropdown", out UITKDropdown uiDropdown) ||
				Character == null ||
				!Character.TryGet(out IPartyController partyController) ||
				partyController.ID < 1)
			{
				return;
			}

			uiDropdown.Hide();

			ClientNamingSystem.GetCharacterID(row.Name.text, (id) =>
			{
				if (Character.ID == id)
				{
					return;
				}

				uiDropdown.AddButton("Message", () =>
				{
					if (UIManager.TryGetTK("UIChat", out UITKChat uiChat))
					{
						uiChat.SetInputText($"/tell {row.Name.text} ");
					}
				});

				uiDropdown.AddButton("Add Friend", () =>
				{
					if (Character.ID != id)
					{
						Client.Broadcast(new FriendAddNewBroadcast()
						{
							CharacterID = id
						}, Channel.Reliable);
					}
				});

				if (Enum.TryParse(row.RankName, out PartyRank rank) &&
					rank < partyController.Rank)
				{
					uiDropdown.AddButton("Promote", () =>
					{
						Client.Broadcast(new PartyChangeRankBroadcast()
						{
							CharacterID = id,
							Rank = PartyRank.Leader,
						}, Channel.Reliable);
					});
					uiDropdown.AddButton("Kick", () =>
					{
						Client.Broadcast(new PartyRemoveBroadcast()
						{
							CharacterID = id,
						}, Channel.Reliable);
					});
				}

				uiDropdown.Show();
			});
		}

		/// <summary>
		/// Removes all member rows.
		/// </summary>
		private void ClearAllMembers()
		{
			foreach (MemberRow row in members.Values)
			{
				row.Root?.RemoveFromHierarchy();
			}
			members.Clear();
			RefreshHeader();
		}
	}
}
