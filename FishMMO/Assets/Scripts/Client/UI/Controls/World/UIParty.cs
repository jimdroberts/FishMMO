using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace FishMMO.Client
{
	public class UIParty : UIControl
	{
		public RectTransform PartyMemberParent;
		public TMP_Text PartyMemberPrefab;
		public Dictionary<long, TMP_Text> Members;

		public override void OnStarting()
		{
			Character character = Character.localCharacter;
			if (character != null)
			{
				character.PartyController.OnPartyCreated += OnPartyCreated;
				character.PartyController.OnLeaveParty += OnLeaveParty;
				character.PartyController.OnAddMember += OnPartyAddMember;
				character.PartyController.OnUpdateMember += OnPartyUpdateMember;
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
				character.PartyController.OnUpdateMember -= OnPartyUpdateMember;
				character.PartyController.OnRemoveMember -= OnPartyRemoveMember;
			}
		}

		public void OnPartyCreated()
		{
			Character character = Character.localCharacter;
			if (character != null && PartyMemberParent != null)
			{
				TMP_Text partyMember = Instantiate(PartyMemberPrefab, PartyMemberParent);
				partyMember.text = character.CharacterName;
				Members.Add(character.ID, partyMember);
			}
		}

		public void OnLeaveParty()
		{
			foreach (TMP_Text member in new List<TMP_Text>(Members.Values))
			{
				Destroy(member.gameObject);
			}
			Members.Clear();
		}

		public void OnPartyAddMember(long characterID, PartyRank rank)
		{
			if (PartyMemberPrefab != null)
			{
				TMP_Text partyMember = Instantiate(PartyMemberPrefab, PartyMemberParent);
				partyMember.text = characterID.ToString();
				Members.Add(characterID, partyMember);
			}
		}

		public void OnPartyUpdateMember(long characterID, PartyRank rank)
		{
			if (Members.TryGetValue(characterID, out TMP_Text text))
			{

			}
		}

		public void OnPartyRemoveMember(long characterID, PartyRank rank)
		{
			if (Members.TryGetValue(characterID, out TMP_Text text))
			{
				Members.Remove(characterID);
				Destroy(text.gameObject);
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
				if (character.TargetController.Current.Target != null)
				{
					Character targetCharacter = character.TargetController.Current.Target.GetComponent<Character>();
					if (targetCharacter != null)
					{
						Client.NetworkManager.ClientManager.Broadcast(new PartyInviteBroadcast()
						{
							targetCharacterId = targetCharacter.ID
						});
					}
				}
			}
		}
	}
}