using FishNet.Connection;
using FishNet.Managing;
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

		private NetworkManager networkManager;

		public override void OnStarting()
		{
			networkManager = FindObjectOfType<NetworkManager>();
			if (networkManager == null)
			{
				Debug.LogError("UICharacterSelect: NetworkManager not found, HUD will not function.");
				return;
			}

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
				partyMember.text = character.characterName;
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
			if (character != null && character.PartyController.current == null && networkManager.IsClient)
			{
				networkManager.ClientManager.Broadcast(new PartyCreateBroadcast());
			}
		}

		public void OnButtonLeaveParty()
		{
			Character character = Character.localCharacter;
			if (character != null && character.PartyController.current != null && networkManager.IsClient)
			{
				networkManager.ClientManager.Broadcast(new PartyLeaveBroadcast());
			}
		}

		public void OnButtonInviteToParty()
		{
			Character character = Character.localCharacter;
			if (character != null && character.PartyController.current != null && networkManager.IsClient)
			{
				if (character.TargetController.current.target != null)
				{
					Character targetCharacter = character.TargetController.current.target.GetComponent<Character>();
					if (targetCharacter != null)
					{
						networkManager.ClientManager.Broadcast(new PartyInviteBroadcast()
						{
							targetCharacterId = targetCharacter.id
						});
					}
				}
			}
		}
	}
}