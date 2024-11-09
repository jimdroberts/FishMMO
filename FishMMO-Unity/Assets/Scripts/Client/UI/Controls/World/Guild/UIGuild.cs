using FishNet.Transporting;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIGuild : UICharacterControl
	{
		public TMP_Text GuildLabel;
		public RectTransform GuildMemberParent;
		public UIGuildMember GuildMemberPrefab;
		public Dictionary<long, UIGuildMember> Members = new Dictionary<long, UIGuildMember>();

		public override void OnDestroying()
		{
			GuildController_OnLeaveGuild();
		}

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

		public void GuildController_OnRemoveMember(long characterID)
		{
			if (Members.TryGetValue(characterID, out UIGuildMember member))
			{
				Members.Remove(characterID);
				Destroy(member.gameObject);
			}
		}

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