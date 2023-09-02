using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace FishMMO.Client
{
	public class UIParty : UIControl
	{
		public RectTransform partyMemberParent;
		public TMP_Text partyMemberPrefab;
		public List<TMP_Text> members;

		public override void OnStarting()
		{
			Character character = Character.localCharacter;
			if (character != null)
			{
				character.PartyController.OnPartyCreated += OnPartyCreated;
				character.PartyController.OnLeaveParty += OnLeaveParty;
				character.PartyController.OnAddMember += OnPartyAddMember;
				character.PartyController.OnRemoveMember += OnPartyRemoveMember;
			}
		}

		public override void OnDestroying()
		{
			Character character = Character.localCharacter;
			if (character != null)
			{
				character.PartyController.OnPartyCreated -= OnPartyCreated;
				character.PartyController.OnLeaveParty -= OnLeaveParty;
				character.PartyController.OnAddMember -= OnPartyAddMember;
				character.PartyController.OnRemoveMember -= OnPartyRemoveMember;
			}
		}

		public void OnPartyCreated()
		{
			Character character = Character.localCharacter;
			if (character != null && partyMemberPrefab != null)
			{
				TMP_Text partyMember = Instantiate(partyMemberPrefab, partyMemberParent);
				partyMember.text = character.CharacterName;
				members.Add(partyMember);
			}
		}

		public void OnLeaveParty()
		{
			foreach (TMP_Text member in members)
			{
				Destroy(member.gameObject);
			}
			members.Clear();
		}

		public void OnPartyAddMember(string partyMemberName, PartyRank rank)
		{
			if (partyMemberPrefab != null)
			{
				TMP_Text partyMember = Instantiate(partyMemberPrefab, partyMemberParent);
				partyMember.text = partyMemberName;
				members.Add(partyMember);
			}
		}

		public void OnPartyRemoveMember(string partyMemberName, PartyRank rank)
		{
			foreach (TMP_Text member in members)
			{
				if (partyMemberName.Equals(member.name))
				{
					members.Remove(member);
					return;
				}
			}
		}

		public void OnButtonCreateParty()
		{
			Character character = Character.localCharacter;
			if (character != null && character.PartyController.Current == null && Client.NetworkManager.IsClient)
			{
				Client.NetworkManager.ClientManager.Broadcast(new PartyCreateBroadcast());
			}
		}

		public void OnButtonLeaveParty()
		{
			Character character = Character.localCharacter;
			if (character != null && character.PartyController.Current != null && Client.NetworkManager.IsClient)
			{
				Client.NetworkManager.ClientManager.Broadcast(new PartyLeaveBroadcast());
			}
		}

		public void OnButtonInviteToParty()
		{
			Character character = Character.localCharacter;
			if (character != null && character.PartyController.Current != null && Client.NetworkManager.IsClient)
			{
#if UNITY_CLIENT
				if (character.TargetController.Current.target != null)
				{
					Character targetCharacter = character.TargetController.Current.target.GetComponent<Character>();
					if (targetCharacter != null)
					{
						Client.NetworkManager.ClientManager.Broadcast(new PartyInviteBroadcast()
						{
							targetCharacterId = targetCharacter.id
						});
					}
				}
#endif
			}
		}
	}
}