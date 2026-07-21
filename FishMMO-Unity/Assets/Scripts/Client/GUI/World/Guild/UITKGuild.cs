using System;
using System.Collections.Generic;
using UnityEngine.UIElements;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Client
{
	/// <summary>
	/// UI Toolkit guild panel. Replaces the legacy UGUI <see cref="UIGuild"/> / <see cref="UIGuildMember"/>:
	/// renders guild members as dynamic rows with name/rank context dropdowns, and handles
	/// create/leave/invite actions. All guild broadcasts are preserved verbatim and the shared UGUI
	/// dialog/dropdown overlays are reused via the UIManager.
	/// </summary>
	public class UITKGuild : UITKCharacterControl
	{
		/// <summary>Name of the guild name label element.</summary>
		private const string GUILD_LABEL_NAME = "guild-name";

		/// <summary>Name of the container that holds the generated member rows.</summary>
		private const string MEMBER_LIST_NAME = "guild-member-list";

		/// <summary>Name of the create-guild button.</summary>
		private const string CREATE_BUTTON_NAME = "guild-create";

		/// <summary>Name of the leave-guild button.</summary>
		private const string LEAVE_BUTTON_NAME = "guild-leave";

		/// <summary>Name of the invite-to-guild button.</summary>
		private const string INVITE_BUTTON_NAME = "guild-invite";

		/// <summary>USS class applied to each generated member row.</summary>
		private const string ROW_CLASS = "guild-member";

		/// <summary>USS class applied to a member row's name label.</summary>
		private const string ROW_NAME_CLASS = "guild-member__name";

		/// <summary>USS class applied to a member row's rank label.</summary>
		private const string ROW_RANK_CLASS = "guild-member__rank";

		/// <summary>USS class applied to a member row's location label.</summary>
		private const string ROW_LOCATION_CLASS = "guild-member__location";

		/// <summary>
		/// Visual elements and state backing a single guild member row.
		/// </summary>
		private sealed class MemberRow
		{
			/// <summary>Root container for the row.</summary>
			public VisualElement Root;
			/// <summary>Member name label.</summary>
			public Label Name;
			/// <summary>Member rank label.</summary>
			public Label Rank;
			/// <summary>Member location label.</summary>
			public Label Location;
			/// <summary>The member's character ID.</summary>
			public long CharacterID;
		}

		/// <summary>All created guild member rows keyed by character ID.</summary>
		private readonly Dictionary<long, MemberRow> members = new Dictionary<long, MemberRow>();
		/// <summary>Label displaying the guild name.</summary>
		private Label guildLabel;
		/// <summary>The container element that holds the generated member rows.</summary>
		private VisualElement memberList;

		/// <summary>
		/// Queries elements and wires up the action buttons.
		/// </summary>
		public override void OnStarting()
		{
			VisualElement root = Root;
			if (root == null)
			{
				return;
			}

			guildLabel = root.Q<Label>(GUILD_LABEL_NAME);
			memberList = root.Q(MEMBER_LIST_NAME);

			Button create = root.Q<Button>(CREATE_BUTTON_NAME);
			if (create != null)
			{
				create.clicked += OnButtonCreateGuild;
			}

			Button leave = root.Q<Button>(LEAVE_BUTTON_NAME);
			if (leave != null)
			{
				leave.clicked += OnButtonLeaveGuild;
			}

			Button invite = root.Q<Button>(INVITE_BUTTON_NAME);
			if (invite != null)
			{
				invite.clicked += OnButtonInviteToGuild;
			}
		}

		/// <summary>
		/// Clears the guild member list when the control is destroyed.
		/// </summary>
		public override void OnDestroying()
		{
			GuildController_OnLeaveGuild();
		}

		/// <summary>
		/// Subscribes to guild controller events after the character is set.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character.TryGet(out IGuildController guildController))
			{
				guildController.OnReceiveGuildInvite += GuildController_OnReceiveGuildInvite;
				guildController.OnAddGuildMember += GuildController_OnAddGuildMember;
				guildController.OnValidateGuildMembers += GuildController_OnValidateGuildMembers;
				guildController.OnRemoveGuildMember += GuildController_OnRemoveMember;
				guildController.OnLeaveGuild += GuildController_OnLeaveGuild;
				guildController.OnReceiveGuildResult += GuildController_OnReceiveGuildResult;
			}
		}

		/// <summary>
		/// Unsubscribes from guild controller events before the character is unset.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			if (Character.TryGet(out IGuildController guildController))
			{
				guildController.OnReceiveGuildInvite -= GuildController_OnReceiveGuildInvite;
				guildController.OnAddGuildMember -= GuildController_OnAddGuildMember;
				guildController.OnValidateGuildMembers -= GuildController_OnValidateGuildMembers;
				guildController.OnRemoveGuildMember -= GuildController_OnRemoveMember;
				guildController.OnLeaveGuild -= GuildController_OnLeaveGuild;
				guildController.OnReceiveGuildResult -= GuildController_OnReceiveGuildResult;
			}
		}

		/// <summary>
		/// Prompts the local player to accept or decline a received guild invite.
		/// </summary>
		/// <param name="inviterCharacterID">The inviter's character ID.</param>
		public void GuildController_OnReceiveGuildInvite(long inviterCharacterID)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, inviterCharacterID, (n) =>
			{
				if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox uiTooltip))
				{
					uiTooltip.Open("You have been invited to join " + n + "'s guild. Would you like to join?",
					() =>
					{
						Client.Broadcast(new GuildAcceptInviteBroadcast(), Channel.Reliable);
					},
					() =>
					{
						Client.Broadcast(new GuildDeclineInviteBroadcast(), Channel.Reliable);
					});
				}
			});
		}

		/// <summary>
		/// Adds a member row and refreshes the guild name label.
		/// </summary>
		/// <param name="characterID">The member's character ID.</param>
		/// <param name="guildID">The guild ID.</param>
		/// <param name="rank">The member's guild rank.</param>
		/// <param name="location">The member's location.</param>
		public void GuildController_OnAddGuildMember(long characterID, long guildID, GuildRank rank, string location)
		{
			GuildController_OnAddMember(characterID, rank, location);

			ClientNamingSystem.SetName(NamingSystemType.GuildName, guildID, (s) =>
			{
				if (guildLabel != null)
				{
					guildLabel.text = s;
				}
			});
		}

		/// <summary>
		/// Removes member rows that are no longer in the validated member set.
		/// </summary>
		/// <param name="newMembers">The set of valid member IDs.</param>
		public void GuildController_OnValidateGuildMembers(HashSet<long> newMembers)
		{
			foreach (long id in new HashSet<long>(members.Keys))
			{
				if (!newMembers.Contains(id))
				{
					GuildController_OnRemoveMember(id);
				}
			}
		}

		/// <summary>
		/// Resets the guild label and clears all member rows when leaving the guild.
		/// </summary>
		public void GuildController_OnLeaveGuild()
		{
			if (guildLabel != null)
			{
				guildLabel.text = "Guild";
			}

			foreach (MemberRow row in members.Values)
			{
				row.Root?.RemoveFromHierarchy();
			}
			members.Clear();
		}

		/// <summary>
		/// Adds or updates a guild member row.
		/// </summary>
		/// <param name="characterID">The member's character ID.</param>
		/// <param name="rank">The member's guild rank.</param>
		/// <param name="location">The member's location.</param>
		public void GuildController_OnAddMember(long characterID, GuildRank rank, string location)
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

			row.Rank.text = rank.ToString();
			row.Location.text = location;
		}

		/// <summary>
		/// Removes a guild member row.
		/// </summary>
		/// <param name="characterID">The member's character ID.</param>
		public void GuildController_OnRemoveMember(long characterID)
		{
			if (members.TryGetValue(characterID, out MemberRow row))
			{
				row.Root?.RemoveFromHierarchy();
				members.Remove(characterID);
			}
		}

		/// <summary>
		/// Displays chat feedback for the result of a guild operation.
		/// </summary>
		/// <param name="result">The guild operation result.</param>
		public void GuildController_OnReceiveGuildResult(GuildResultType result)
		{
			if (!UIManager.TryGetTK("UIChat", out UITKChat chat))
			{
				return;
			}
			switch (result)
			{
				case GuildResultType.Success:
					break;
				case GuildResultType.InvalidGuildName:
					chat.InstantiateChatMessage(ChatChannel.System, "", "The requested guild name is invalid.");
					break;
				case GuildResultType.NameAlreadyExists:
					chat.InstantiateChatMessage(ChatChannel.System, "", "A guild with that name already exists.");
					break;
				case GuildResultType.AlreadyInGuild:
					chat.InstantiateChatMessage(ChatChannel.System, "", "You are already in a guild!");
					break;
				default:
					return;
			}
		}

		/// <summary>
		/// Prompts for a guild name and broadcasts a create-guild request.
		/// </summary>
		public void OnButtonCreateGuild()
		{
			if (Character != null &&
				Character.TryGet(out IGuildController guildController) &&
				guildController.ID < 1 && Client.NetworkManager.IsClientStarted)
			{
				if (UIManager.TryGetTK("UIDialogInputBox", out UITKDialogInputBox tooltip))
				{
					tooltip.Open("Please type the name of your new guild!", (s) =>
					{
						if (Authentication.IsAllowedGuildName(s))
						{
							Client.Broadcast(new GuildCreateBroadcast()
							{
								GuildName = s,
							}, Channel.Reliable);
						}
					}, null);
				}
			}
		}

		/// <summary>
		/// Confirms then broadcasts a leave-guild request.
		/// </summary>
		public void OnButtonLeaveGuild()
		{
			if (Character != null &&
				Character.TryGet(out IGuildController guildController) &&
				guildController.ID > 0 && Client.NetworkManager.IsClientStarted)
			{
				if (UIManager.TryGetTK("UIDialogBox", out UITKDialogBox tooltip))
				{
					tooltip.Open("Are you sure you want to leave your guild?", () =>
					{
						Client.Broadcast(new GuildLeaveBroadcast(), Channel.Reliable);
					}, () => { });
				}
			}
		}

		/// <summary>
		/// Invites the current target, or prompts for a name, to the guild.
		/// </summary>
		public void OnButtonInviteToGuild()
		{
			if (Character != null &&
				Character.TryGet(out IGuildController guildController) &&
				guildController.ID > 0 &&
				Client.NetworkManager.IsClientStarted)
			{
				if (Character.TryGet(out ITargetController targetController) &&
					targetController.Current.Target != null)
				{
					IPlayerCharacter targetCharacter = targetController.Current.Target.GetComponent<IPlayerCharacter>();
					if (targetCharacter != null)
					{
						Client.Broadcast(new GuildInviteBroadcast()
						{
							TargetCharacterID = targetCharacter.ID
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
										Client.Broadcast(new GuildInviteBroadcast()
										{
											TargetCharacterID = id,
										}, Channel.Reliable);
									}
									else if (UIManager.TryGetTK("UIChat", out UITKChat chat))
									{
										chat.InstantiateChatMessage(ChatChannel.System, "", "You can't invite yourself to the guild.");
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
			rowRoot.AddToClassList(ROW_CLASS);

			Label name = new Label();
			name.AddToClassList(ROW_NAME_CLASS);
			rowRoot.Add(name);

			Label rank = new Label();
			rank.AddToClassList(ROW_RANK_CLASS);
			rowRoot.Add(rank);

			Label location = new Label();
			location.AddToClassList(ROW_LOCATION_CLASS);
			rowRoot.Add(location);

			MemberRow row = new MemberRow
			{
				Root = rowRoot,
				Name = name,
				Rank = rank,
				Location = location,
				CharacterID = characterID,
			};

			name.RegisterCallback<PointerDownEvent>(evt => OnMemberNamePointerDown(evt, row));
			rank.RegisterCallback<PointerDownEvent>(evt => OnMemberRankPointerDown(evt, row));

			memberList.Add(rowRoot);
			members.Add(characterID, row);
			return row;
		}

		/// <summary>
		/// Opens the name context dropdown (message / add friend) on left-click.
		/// </summary>
		/// <param name="evt">The pointer-down event.</param>
		/// <param name="row">The member row.</param>
		private void OnMemberNamePointerDown(PointerDownEvent evt, MemberRow row)
		{
			if (evt.button != 0)
			{
				return;
			}

			if (!UIManager.TryGetTK("UIDropdown", out UITKDropdown uiDropdown) || Character == null)
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

				uiDropdown.Show();
			});
		}

		/// <summary>
		/// Opens the rank context dropdown (promote / demote / kick) on left-click if allowed.
		/// </summary>
		/// <param name="evt">The pointer-down event.</param>
		/// <param name="row">The member row.</param>
		private void OnMemberRankPointerDown(PointerDownEvent evt, MemberRow row)
		{
			if (evt.button != 0)
			{
				return;
			}

			if (!UIManager.TryGetTK("UIDropdown", out UITKDropdown uiDropdown) ||
				Character == null ||
				!Character.TryGet(out IGuildController guildController) ||
				guildController.ID < 1)
			{
				return;
			}

			uiDropdown.Hide();

			if (Enum.TryParse(row.Rank.text, out GuildRank rank) &&
				rank < guildController.Rank)
			{
				GuildRank nextRank = rank + 1;
				GuildRank prevRank = rank - 1;

				if (nextRank < guildController.Rank)
				{
					uiDropdown.AddButton("Promote", () =>
					{
						ClientNamingSystem.GetCharacterID(row.Name.text, (id) =>
						{
							Client.Broadcast(new GuildChangeRankBroadcast()
							{
								CharacterID = id,
								Rank = nextRank,
							}, Channel.Reliable);
						});
					});
				}

				if (prevRank > GuildRank.None)
				{
					uiDropdown.AddButton("Demote", () =>
					{
						ClientNamingSystem.GetCharacterID(row.Name.text, (id) =>
						{
							Client.Broadcast(new GuildChangeRankBroadcast()
							{
								CharacterID = id,
								Rank = prevRank,
							}, Channel.Reliable);
						});
					});
				}

				uiDropdown.AddButton("Kick", () =>
				{
					ClientNamingSystem.GetCharacterID(row.Name.text, (id) =>
					{
						Client.Broadcast(new GuildRemoveBroadcast()
						{
							CharacterID = id,
						}, Channel.Reliable);
					});
				});
			}

			if (uiDropdown.Buttons.Count > 0 ||
				uiDropdown.Toggles.Count > 0)
			{
				uiDropdown.Show();
			}
		}
	}
}
