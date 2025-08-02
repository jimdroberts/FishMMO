using FishNet.Transporting;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UI control for managing and displaying guild information and members.
	/// </summary>
	public class UIGuild : UICharacterControl
	{
		/// <summary>
		/// The label displaying the guild name.
		/// </summary>
		public TMP_Text GuildLabel;
		/// <summary>
		/// The parent transform for guild member UI elements.
		/// </summary>
		public RectTransform GuildMemberParent;
		/// <summary>
		/// Prefab used to instantiate guild member UI elements.
		/// </summary>
		public UIGuildMember GuildMemberPrefab;
		/// <summary>
		/// Dictionary of guild members by character ID.
		/// </summary>
		public Dictionary<long, UIGuildMember> Members = new Dictionary<long, UIGuildMember>();

		/// <summary>
		/// Called when the UI is being destroyed. Cleans up guild members.
		/// </summary>
		public override void OnDestroying()
		{
			GuildController_OnLeaveGuild();
		}

		/// <summary>
		/// Called after setting the character reference. Subscribes to guild controller events.
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
		/// Called before unsetting the character reference. Unsubscribes from guild controller events.
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
		/// Handles receiving a guild invite and shows a dialog to accept or decline.
		/// </summary>
		/// <param name="inviterCharacterID">The character ID of the inviter.</param>
		public void GuildController_OnReceiveGuildInvite(long inviterCharacterID)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, inviterCharacterID, (n) =>
			{
				if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiTooltip))
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
		/// Handles adding a guild member and updates the guild label.
		/// </summary>
		/// <param name="characterID">The character ID of the new member.</param>
		/// <param name="guildID">The guild ID.</param>
		/// <param name="rank">The rank of the new member.</param>
		/// <param name="location">The location of the new member.</param>
		public void GuildController_OnAddGuildMember(long characterID, long guildID, GuildRank rank, string location)
		{
			GuildController_OnAddMember(characterID, rank, location);

			ClientNamingSystem.SetName(NamingSystemType.GuildName, guildID, (s) =>
			{
				if (GuildLabel != null)
				{
					GuildLabel.text = s;
				}
			});
		}

		/// <summary>
		/// Validates the current guild members against a new set and removes any that are no longer present.
		/// </summary>
		/// <param name="newMembers">The set of valid member IDs.</param>
		public void GuildController_OnValidateGuildMembers(HashSet<long> newMembers)
		{
			foreach (long id in new HashSet<long>(Members.Keys))
			{
				if (!newMembers.Contains(id))
				{
					GuildController_OnRemoveMember(id);
				}
			}
		}

		/// <summary>
		/// Handles leaving the guild. Clears the guild label and member list.
		/// </summary>
		public void GuildController_OnLeaveGuild()
		{
			if (GuildLabel != null)
			{
				GuildLabel.text = "Guild";
			}
			foreach (UIGuildMember member in new List<UIGuildMember>(Members.Values))
			{
				Destroy(member.gameObject);
			}
			Members.Clear();
		}

		/// <summary>
		/// Adds a guild member UI element to the member list.
		/// </summary>
		/// <param name="characterID">The character ID of the member.</param>
		/// <param name="rank">The rank of the member.</param>
		/// <param name="location">The location of the member.</param>
		public void GuildController_OnAddMember(long characterID, GuildRank rank, string location)
		{
			if (GuildMemberPrefab != null && GuildMemberParent != null)
			{
				if (!Members.TryGetValue(characterID, out UIGuildMember guildMember))
				{
					guildMember = Instantiate(GuildMemberPrefab, GuildMemberParent);
					guildMember.gameObject.SetActive(true);
					Members.Add(characterID, guildMember);
				}
				if (guildMember.Name != null)
				{
					ClientNamingSystem.SetName(NamingSystemType.CharacterName, characterID, (n) =>
					{
						guildMember.Name.text = n;
					});
				}
				if (guildMember.Rank != null)
					guildMember.Rank.text = rank.ToString();
				if (guildMember.Location != null)
					guildMember.Location.text = location;
			}
		}

		/// <summary>
		/// Removes a guild member UI element from the member list.
		/// </summary>
		/// <param name="characterID">The character ID of the member to remove.</param>
		public void GuildController_OnRemoveMember(long characterID)
		{
			if (Members.TryGetValue(characterID, out UIGuildMember member))
			{
				Members.Remove(characterID);
				Destroy(member.gameObject);
			}
		}

		/// <summary>
		/// Handles the result of a guild operation and displays appropriate chat messages.
		/// </summary>
		/// <param name="result">The result type of the guild operation.</param>
		public void GuildController_OnReceiveGuildResult(GuildResultType result)
		{
			if (!UIManager.TryGet("UIChat", out UIChat chat))
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
				default: return;
			}
		}

		/// <summary>
		/// Handles the create guild button click. Prompts for a guild name and sends a create request.
		/// </summary>
		public void OnButtonCreateGuild()
		{
			if (Character != null &&
				Character.TryGet(out IGuildController guildController) &&
				guildController.ID < 1 && Client.NetworkManager.IsClientStarted)
			{
				if (UIManager.TryGet("UIDialogInputBox", out UIDialogInputBox tooltip))
				{
					tooltip.Open("Please type the name of your new guild!", (s) =>
					{
						if (Constants.Authentication.IsAllowedGuildName(s))
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
		/// Handles the leave guild button click. Prompts for confirmation and sends a leave request.
		/// </summary>
		public void OnButtonLeaveGuild()
		{
			if (Character != null &&
				Character.TryGet(out IGuildController guildController) &&
				guildController.ID > 0 && Client.NetworkManager.IsClientStarted)
			{
				if (UIManager.TryGet("UIDialogBox", out UIDialogBox tooltip))
				{
					tooltip.Open("Are you sure you want to leave your guild?", () =>
					{
						Client.Broadcast(new GuildLeaveBroadcast(), Channel.Reliable);
					}, () => { });
				}
			}
		}

		/// <summary>
		/// Handles the invite to guild button click. Prompts for a target or uses the current target, then sends an invite request.
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

				if (UIManager.TryGet("UIDialogInputBox", out UIDialogInputBox tooltip))
				{
					tooltip.Open("Please type the name of the person you wish to invite.", (s) =>
					{
						if (Constants.Authentication.IsAllowedCharacterName(s))
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
									else if (UIManager.TryGet("UIChat", out UIChat chat))
									{
										chat.InstantiateChatMessage(ChatChannel.System, "", "You can't invite yourself to the guild.");
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