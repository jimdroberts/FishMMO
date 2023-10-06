using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Client
{
	public class UIParty : UIControl
	{
		public RectTransform PartyMemberParent;
		public UIPartyMember PartyMemberPrefab;
		public Dictionary<long, UIPartyMember> Members = new Dictionary<long, UIPartyMember>();

		public override void OnStarting()
		{
		}

		public override void OnDestroying()
		{
		}

		public void OnPartyCreated(string location)
		{
			Character character = Character.localCharacter;
			if (character != null && PartyMemberPrefab != null && PartyMemberParent != null)
			{
				UIPartyMember member = Instantiate(PartyMemberPrefab, PartyMemberParent);
				if (member != null)
				{
					if (member.Name != null)
						member.Name.text = "Name: " + character.CharacterName;
					if (member.Rank != null)
						member.Rank.text = "Rank: " + character.PartyController.Rank.ToString();
					if (member.Location != null)
						member.Location.text = "Location: " + location;
					Members.Add(character.ID, member);
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

		public void OnPartyAddMember(long characterID, PartyRank rank, string location)
		{
			if (PartyMemberPrefab != null && PartyMemberParent != null)
			{
				if (Members.TryGetValue(characterID, out UIPartyMember partyMember))
				{
					if (partyMember.Name != null)
						partyMember.Name.text = characterID.ToString();
					if (partyMember.Rank != null)
						partyMember.Rank.text = "Rank: " + rank.ToString();
					if (partyMember.Location != null)
						partyMember.Location.text = "Location: " + location;
				}
				else
				{
					UIPartyMember member = Instantiate(PartyMemberPrefab, PartyMemberParent);
					if (member != null)
					{
						if (member.Name != null)
							member.Name.text = characterID.ToString();
						if (member.Rank != null)
							member.Rank.text = "Rank: " + rank.ToString();
						if (member.Location != null)
							member.Location.text = "Location: " + location;
						Members.Add(characterID, member);
					}
				}
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
			Character character = Character.localCharacter;
			if (character != null && character.PartyController.ID < 1 && Client.NetworkManager.IsClient)
			{
				Client.NetworkManager.ClientManager.Broadcast(new PartyCreateBroadcast());
			}
		}

		public void OnButtonLeaveParty()
		{
			Character character = Character.localCharacter;
			if (character != null && character.PartyController.ID > 0 && Client.NetworkManager.IsClient)
			{
				if (UIManager.TryGet("UIConfirmationTooltip", out UIConfirmationTooltip tooltip))
				{
					tooltip.Open("Are you sure you want to leave your party?", () =>
					{
						Client.NetworkManager.ClientManager.Broadcast(new PartyLeaveBroadcast());
					}, null);
				}
			}
		}

		public void OnButtonInviteToParty()
		{
			Character character = Character.localCharacter;
			if (character != null && character.PartyController.ID > 0 && Client.NetworkManager.IsClient)
			{
				if (character.TargetController.Current.Target != null)
				{
					Character targetCharacter = character.TargetController.Current.Target.GetComponent<Character>();
					if (targetCharacter != null)
					{
						Client.NetworkManager.ClientManager.Broadcast(new PartyInviteBroadcast()
						{
							targetCharacterID = targetCharacter.ID
						});
					}
				}
			}
		}
	}
}