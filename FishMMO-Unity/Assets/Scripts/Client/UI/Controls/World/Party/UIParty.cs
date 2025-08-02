using FishNet.Transporting;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// UIParty class handles the user interface for party management, including displaying party members,
	/// and handling party-related actions such as creating a party, leaving a party, and inviting members.
	/// </summary>
	public class UIParty : UICharacterControl
	{
		/// <summary>
		/// The parent RectTransform for party member UI elements.
		/// </summary>
		public RectTransform PartyMemberParent;
		/// <summary>
		/// The prefab used to instantiate party member UI elements.
		/// </summary>
		public UIPartyMember PartyMemberPrefab;
		/// <summary>
		/// Dictionary of party members, keyed by character ID.
		/// </summary>
		public Dictionary<long, UIPartyMember> Members = new Dictionary<long, UIPartyMember>();

		/// <summary>
		/// Called when the party UI is being destroyed. Cleans up all member UI elements.
		/// </summary>
		public override void OnDestroying()
		{
			foreach (UIPartyMember member in new List<UIPartyMember>(Members.Values))
			{
				Destroy(member.gameObject);
			}
			Members.Clear();
		}

		/// <summary>
		/// Called after the character is set. Subscribes to party controller events.
		/// </summary>
		public override void OnPostSetCharacter()
		{
			base.OnPostSetCharacter();

			if (Character.TryGet(out IPartyController partyController))
			{
				partyController.OnPartyCreated += OnPartyCreated;
				partyController.OnReceivePartyInvite += PartyController_OnReceivePartyInvite;
				partyController.OnAddPartyMember += OnPartyAddMember;
				partyController.OnValidatePartyMembers += PartyController_OnValidatePartyMembers;
				partyController.OnRemovePartyMember += OnPartyRemoveMember;
				partyController.OnLeaveParty += OnLeaveParty;
			}
		}

		/// <summary>
		/// Called before the character is unset. Unsubscribes from party controller events.
		/// </summary>
		public override void OnPreUnsetCharacter()
		{
			base.OnPreUnsetCharacter();

			if (Character.TryGet(out IPartyController partyController))
			{
				partyController.OnPartyCreated -= OnPartyCreated;
				partyController.OnReceivePartyInvite -= PartyController_OnReceivePartyInvite;
				partyController.OnAddPartyMember -= OnPartyAddMember;
				partyController.OnValidatePartyMembers -= PartyController_OnValidatePartyMembers;
				partyController.OnRemovePartyMember -= OnPartyRemoveMember;
				partyController.OnLeaveParty -= OnLeaveParty;
			}
		}

		/// <summary>
		/// Handles receiving a party invite. Opens a dialog box to accept or decline.
		/// </summary>
		/// <param name="inviterCharacterID">The character ID of the inviter.</param>
		public void PartyController_OnReceivePartyInvite(long inviterCharacterID)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, inviterCharacterID, (n) =>
			{
				if (UIManager.TryGet("UIDialogBox", out UIDialogBox uiTooltip))
				{
					uiTooltip.Open("You have been invited to join " + n + "'s party. Would you like to join?",
					() =>
					{
						Client.Broadcast(new PartyAcceptInviteBroadcast(), Channel.Reliable);
					},
					() =>
					{
						Client.Broadcast(new PartyDeclineInviteBroadcast(), Channel.Reliable);
					});
				}
			});
		}

		/// <summary>
		/// Validates the current party members against a new set, removing any that are no longer present.
		/// </summary>
		/// <param name="newMembers">Set of valid member IDs.</param>
		public void PartyController_OnValidatePartyMembers(HashSet<long> newMembers)
		{
			foreach (long id in new HashSet<long>(Members.Keys))
			{
				if (!newMembers.Contains(id))
				{
					OnPartyRemoveMember(id);
				}
			}
		}

		/// <summary>
		/// Handles party creation. Instantiates and adds the local player as a party member.
		/// </summary>
		/// <param name="location">Location string (unused).</param>
		public void OnPartyCreated(string location)
		{
			if (Character != null &&
				PartyMemberPrefab != null &&
				PartyMemberParent != null &&
				Character.TryGet(out IPartyController partyController))
			{
				UIPartyMember member = Instantiate(PartyMemberPrefab, PartyMemberParent);
				if (member != null)
				{
					if (member.Name != null)
						member.Name.text = Character.CharacterName;
					if (member.Rank != null)
					{
						member.Rank.gameObject.name = partyController.Rank.ToString();
						member.Rank.text = partyController.Rank == PartyRank.Leader ? "*" : "";
					}
					if (member.Health != null)
						member.Health.value = Character.TryGet(out ICharacterAttributeController attributeController) ? attributeController.GetHealthResourceAttributeCurrentPercentage() : 0.0f;

					member.gameObject.SetActive(true);
					Members.Add(Character.ID, member);
				}
			}
		}

		/// <summary>
		/// Handles leaving the party. Cleans up all member UI elements.
		/// </summary>
		public void OnLeaveParty()
		{
			foreach (UIPartyMember member in new List<UIPartyMember>(Members.Values))
			{
				Destroy(member.gameObject);
			}
			Members.Clear();
		}

		/// <summary>
		/// Adds a new member to the party UI.
		/// </summary>
		/// <param name="characterID">The character ID of the new member.</param>
		/// <param name="rank">The rank of the new member.</param>
		/// <param name="healthPCT">The health percentage of the new member.</param>
		public void OnPartyAddMember(long characterID, PartyRank rank, float healthPCT)
		{
			if (PartyMemberPrefab != null && PartyMemberParent != null)
			{
				if (!Members.TryGetValue(characterID, out UIPartyMember partyMember))
				{
					partyMember = Instantiate(PartyMemberPrefab, PartyMemberParent);
					partyMember.gameObject.SetActive(true);
					Members.Add(characterID, partyMember);
				}
				if (partyMember.Name != null)
				{
					ClientNamingSystem.SetName(NamingSystemType.CharacterName, characterID, (n) =>
					{
						partyMember.Name.text = n;
					});
				}
				if (partyMember.Rank != null)
				{
					partyMember.Rank.gameObject.name = rank.ToString();
					partyMember.Rank.text = rank == PartyRank.Leader ? "*" : "";
				}
				if (partyMember.Health != null)
					partyMember.Health.value = healthPCT;
			}
		}

		/// <summary>
		/// Removes a member from the party UI.
		/// </summary>
		/// <param name="characterID">The character ID of the member to remove.</param>
		public void OnPartyRemoveMember(long characterID)
		{
			if (Members.TryGetValue(characterID, out UIPartyMember member))
			{
				Members.Remove(characterID);
				Destroy(member.gameObject);
			}
		}

		/// <summary>
		/// Called when the create party button is pressed. Broadcasts a create party request if not already in a party.
		/// </summary>
		public void OnButtonCreateParty()
		{
			if (Character != null &&
				Character.TryGet(out IPartyController partyController) &&
				partyController.ID < 1)
			{
				Client.Broadcast(new PartyCreateBroadcast(), Channel.Reliable);
			}
		}

		/// <summary>
		/// Called when the leave party button is pressed. Opens a confirmation dialog and broadcasts leave request.
		/// </summary>
		public void OnButtonLeaveParty()
		{
			if (Character != null &&
				Character.TryGet(out IPartyController partyController) &&
				partyController.ID > 0)
			{
				if (UIManager.TryGet("UIDialogBox", out UIDialogBox tooltip))
				{
					tooltip.Open("Are you sure you want to leave your party?", () =>
					{
						Client.Broadcast(new PartyLeaveBroadcast(), Channel.Reliable);
					}, () => { });
				}
			}
		}

		/// <summary>
		/// Called when the invite to party button is pressed. Broadcasts an invite to the selected target or prompts for a name.
		/// </summary>
		public void OnButtonInviteToParty()
		{
			if (Character != null &&
				Character.TryGet(out IPartyController partyController) &&
				partyController.ID > 0 &&
				Client.NetworkManager.IsClientStarted)
			{
				if (Character.TryGet(out ITargetController targetController) &&
					targetController.Current.Target != null)
				{
					IPlayerCharacter targetCharacter = targetController.Current.Target.GetComponent<IPlayerCharacter>();
					if (targetCharacter != null)
					{
						Client.Broadcast(new PartyInviteBroadcast()
						{
							TargetCharacterID = targetCharacter.ID,
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
										Client.Broadcast(new PartyInviteBroadcast()
										{
											TargetCharacterID = id,
										}, Channel.Reliable);
									}
									else if (UIManager.TryGet("UIChat", out UIChat chat))
									{
										chat.InstantiateChatMessage(ChatChannel.System, "", "You can't invite yourself to the party.");
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