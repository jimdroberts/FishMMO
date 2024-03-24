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
			OnLeaveGuild();
		}

		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character.TryGet(out GuildController guildController))
			{
				guildController.OnReceiveGuildInvite += GuildController_OnReceiveGuildInvite;
				guildController.OnAddGuildMember += GuildController_OnAddGuildMember;
				guildController.OnValidateGuildMembers += GuildController_OnValidateGuildMembers;
				guildController.OnRemoveGuildMember += OnGuildRemoveMember;
				guildController.OnLeaveGuild += OnLeaveGuild;
			}
		}

		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			if (Character.TryGet(out GuildController guildController))
			{
				guildController.OnReceiveGuildInvite -= GuildController_OnReceiveGuildInvite;
				guildController.OnAddGuildMember -= GuildController_OnAddGuildMember;
				guildController.OnValidateGuildMembers -= GuildController_OnValidateGuildMembers;
				guildController.OnRemoveGuildMember -= OnGuildRemoveMember;
				guildController.OnLeaveGuild -= OnLeaveGuild;
			}
		}

		public void GuildController_OnReceiveGuildInvite(long inviterCharacterID)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, inviterCharacterID, (n) =>
			{
				if (UIManager.TryGet("UIConfirmationTooltip", out UIConfirmationTooltip uiTooltip))
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
			OnGuildAddMember(characterID, rank, location);

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
					OnGuildRemoveMember(id);
				}
			}
		}

		public void OnLeaveGuild()
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

		public void OnGuildAddMember(long characterID, GuildRank rank, string location)
		{
			if (GuildMemberPrefab != null && GuildMemberParent != null)
			{
				if (!Members.TryGetValue(characterID, out UIGuildMember guildMember))
				{
					Members.Add(characterID, guildMember = Instantiate(GuildMemberPrefab, GuildMemberParent));
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

		public void OnGuildRemoveMember(long characterID)
		{
			if (Members.TryGetValue(characterID, out UIGuildMember member))
			{
				Members.Remove(characterID);
				Destroy(member.gameObject);
			}
		}

		public void OnButtonCreateGuild()
		{
			if (Character != null &&
				Character.TryGet(out GuildController guildController) &&
				guildController.ID < 1 && Client.NetworkManager.IsClientStarted)
			{
				if (UIManager.TryGet("UIInputConfirmationTooltip", out UIInputConfirmationTooltip tooltip))
				{
					tooltip.Open("Please type the name of your new guild!", (s) =>
					{
						if (Constants.Authentication.IsAllowedGuildName(s))
						{
							Client.Broadcast(new GuildCreateBroadcast()
							{
								guildName = s,
							}, Channel.Reliable);
						}
					}, null);
				}
			}
		}

		public void OnButtonLeaveGuild()
		{
			if (Character != null &&
				Character.TryGet(out GuildController guildController) &&
				guildController.ID > 0 && Client.NetworkManager.IsClientStarted)
			{
				if (UIManager.TryGet("UIConfirmationTooltip", out UIConfirmationTooltip tooltip))
				{
					tooltip.Open("Are you sure you want to leave your guild?", () =>
					{
						Client.Broadcast(new GuildLeaveBroadcast(), Channel.Reliable);
					}, null);
				}
			}
		}

		public void OnButtonInviteToGuild()
		{
			if (Character != null &&
				Character.TryGet(out GuildController guildController) &&
				guildController.ID > 0 &&
				Client.NetworkManager.IsClientStarted)
			{
				if (Character.TryGet(out TargetController targetController) &&
					targetController.Current.Target != null)
				{
					Character targetCharacter = targetController.Current.Target.GetComponent<Character>();
					if (targetCharacter != null)
					{
						Client.Broadcast(new GuildInviteBroadcast()
						{
							targetCharacterID = targetCharacter.ID
						}, Channel.Reliable);
					}
				}
				else if (UIManager.TryGet("UIInputConfirmationTooltip", out UIInputConfirmationTooltip tooltip))
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
											targetCharacterID = id,
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