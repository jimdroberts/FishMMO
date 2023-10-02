using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Client
{
	public class UIGuild : UIControl
	{
		public RectTransform GuildMemberParent;
		public UIGuildMember GuildMemberPrefab;
		public Dictionary<long, UIGuildMember> Members;

		public override void OnStarting()
		{
			Character character = Character.localCharacter;
			if (character != null)
			{
				character.GuildController.OnGuildCreated += OnGuildCreated;
				character.GuildController.OnLeaveGuild += OnLeaveGuild;
				character.GuildController.OnAddMember += OnGuildAddMember;
				character.GuildController.OnRemoveMember += OnGuildRemoveMember;
			}
		}

		public override void OnDestroying()
		{
			Character character = Character.localCharacter;
			if (character != null)
			{
				character.GuildController.OnGuildCreated -= OnGuildCreated;
				character.GuildController.OnLeaveGuild -= OnLeaveGuild;
				character.GuildController.OnAddMember -= OnGuildAddMember;
				character.GuildController.OnRemoveMember -= OnGuildRemoveMember;
			}
		}

		public void OnGuildCreated()
		{
			Character character = Character.localCharacter;
			if (character != null && GuildMemberPrefab != null && GuildMemberParent != null)
			{
				UIGuildMember partyMember = Instantiate(GuildMemberPrefab, GuildMemberParent);
				if (partyMember != null)
				{
					if (partyMember.Name != null)
						partyMember.Name.text = character.CharacterName;
					if (partyMember.Rank != null)
						partyMember.Rank.text = "Rank: " + GuildRank.Leader.ToString();
					if (partyMember.Location != null)
						partyMember.Location.text = "Location: " + character.gameObject.scene.name;
					Members.Add(character.ID, partyMember);
				}
			}
		}

		public void OnLeaveGuild()
		{
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
				UIGuildMember partyMember = Instantiate(GuildMemberPrefab, GuildMemberParent);
				if (partyMember != null)
				{
					if (partyMember.Name != null)
						partyMember.Name.text = characterID.ToString();
					if (partyMember.Rank != null)
						partyMember.Rank.text = "Rank: " + rank.ToString();
					if (partyMember.Location != null)
						partyMember.Location.text = "Location: " + location;
					Members.Add(characterID, partyMember);
				}
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
			Character character = Character.localCharacter;
			if (character != null && character.PartyController.Current == null && Client.NetworkManager.IsClient)
			{
				if (UIManager.TryGet("UIInputConfirmationTooltip", out UIInputConfirmationTooltip tooltip))
				{
					tooltip.Open("Please type the name of your new guild!", (s) =>
					{
						Client.NetworkManager.ClientManager.Broadcast(new GuildCreateBroadcast()
						{
							guildName = s,
						});
					}, null);
				}
			}
		}

		public void OnButtonLeaveGuild()
		{
			Character character = Character.localCharacter;
			if (character != null && character.PartyController.Current != null && Client.NetworkManager.IsClient)
			{
				if (UIManager.TryGet("UIConfirmationTooltip", out UIConfirmationTooltip tooltip))
				{
					tooltip.Open("Are you sure you want to leave your guild?", () =>
					{
						Client.NetworkManager.ClientManager.Broadcast(new GuildLeaveBroadcast());
					}, null);
				}
				
			}
		}

		public void OnButtonInviteToGuild()
		{
			Character character = Character.localCharacter;
			if (character != null && character.PartyController.Current != null && Client.NetworkManager.IsClient)
			{
				if (character.TargetController.Current.Target != null)
				{
					Character targetCharacter = character.TargetController.Current.Target.GetComponent<Character>();
					if (targetCharacter != null)
					{
						Client.NetworkManager.ClientManager.Broadcast(new GuildInviteBroadcast()
						{
							targetCharacterID = targetCharacter.ID
						});
					}
				}
			}
		}

		public void OnClose()
		{
			Visible = false;
		}
	}
}