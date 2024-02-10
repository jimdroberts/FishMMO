using FishNet.Transporting;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIParty : UICharacterControl
	{
		public CharacterAttributeTemplate HealthTemplate;
		public RectTransform PartyMemberParent;
		public UIPartyMember PartyMemberPrefab;
		public Dictionary<long, UIPartyMember> Members = new Dictionary<long, UIPartyMember>();

		public override void OnDestroying()
		{
			foreach (UIPartyMember member in new List<UIPartyMember>(Members.Values))
			{
				Destroy(member.gameObject);
			}
			Members.Clear();
		}

		public void OnPartyCreated(string location)
		{
			if (Character != null && PartyMemberPrefab != null && PartyMemberParent != null)
			{
				UIPartyMember member = Instantiate(PartyMemberPrefab, PartyMemberParent);
				if (member != null)
				{
					if (member.Name != null)
						member.Name.text = Character.CharacterName;
					if (member.Rank != null)
						member.Rank.text = "Rank: " + (Character.TryGet(out PartyController partyController) ? partyController.Rank.ToString() : "");
					if (member.Health != null)
						member.Health.value = Character.TryGet(out CharacterAttributeController attributeController) ? attributeController.GetResourceAttributeCurrentPercentage(HealthTemplate) : 0.0f;
					Members.Add(Character.ID, member);
				}
			}
		}

		public void OnLeaveParty()
		{
			foreach (UIPartyMember member in new List<UIPartyMember>(Members.Values))
			{
				Destroy(member.gameObject);
			}
			Members.Clear();
		}

		public void OnPartyAddMember(long characterID, PartyRank rank, float healthPCT)
		{
			if (PartyMemberPrefab != null && PartyMemberParent != null)
			{
				if (!Members.TryGetValue(characterID, out UIPartyMember partyMember))
				{
					Members.Add(characterID, partyMember = Instantiate(PartyMemberPrefab, PartyMemberParent));
				}
				if (partyMember.Name != null)
				{
					ClientNamingSystem.SetName(NamingSystemType.CharacterName, characterID, (n) =>
					{
						partyMember.Name.text = n;
					});
				}
				if (partyMember.Rank != null)
					partyMember.Rank.text = "Rank: " + rank.ToString();
				if (partyMember.Health != null)
					partyMember.Health.value = healthPCT;
			}
		}

		public void OnPartyRemoveMember(long characterID)
		{
			if (Members.TryGetValue(characterID, out UIPartyMember member))
			{
				Members.Remove(characterID);
				Destroy(member.gameObject);
			}
		}

		public void OnButtonCreateParty()
		{
			if (Character != null &&
				Character.TryGet(out PartyController partyController) &&
				partyController.ID < 1)
			{
				Client.Broadcast(new PartyCreateBroadcast(), Channel.Reliable);
			}
		}

		public void OnButtonLeaveParty()
		{
			if (Character != null &&
				Character.TryGet(out PartyController partyController) &&
				partyController.ID > 0)
			{
				if (UIManager.TryGet("UIConfirmationTooltip", out UIConfirmationTooltip tooltip))
				{
					tooltip.Open("Are you sure you want to leave your party?", () =>
					{
						Client.Broadcast(new PartyLeaveBroadcast(), Channel.Reliable);
					}, null);
				}
			}
		}

		public void OnButtonInviteToParty()
		{
			if (Character != null &&
				Character.TryGet(out PartyController partyController) &&
				partyController.ID > 0 &&
				Client.NetworkManager.IsClientStarted)
			{
				if (Character.TryGet(out TargetController targetController) &&
					targetController.Current.Target != null)
				{
					Character targetCharacter = targetController.Current.Target.GetComponent<Character>();
					if (targetCharacter != null)
					{
						Client.Broadcast(new PartyInviteBroadcast()
						{
							targetCharacterID = targetCharacter.ID,
						}, Channel.Reliable);
					}
				}
				else if (UIManager.TryGet("UIInputConfirmationTooltip", out UIInputConfirmationTooltip tooltip))
				{
					tooltip.Open("Please type the name of the person you wish to invite.", (s) =>
					{
						if (Constants.Authentication.IsAllowedCharacterName(s) &&
							ClientNamingSystem.GetCharacterID(s, out long id))
						{
							Client.Broadcast(new PartyInviteBroadcast()
							{
								targetCharacterID = id,
							}, Channel.Reliable);
						}
						else if (UIManager.TryGet("UIChat", out UIChat chat))
						{
							chat.InstantiateChatMessage(ChatChannel.System, "", "A person with that name could not be found. Are you sure you have encountered them?");
						}
					}, null);
				}
			}
		}
	}
}