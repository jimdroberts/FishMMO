using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FishNet.Transporting;
using FishMMO.Shared;

namespace FishMMO.Client
{
	public class UIPartyMember : MonoBehaviour
	{
		/// <summary>
		/// The label displaying the party member's name.
		/// </summary>
		public TMP_Text Name;
		/// <summary>
		/// The label displaying the party member's rank.
		/// </summary>
		public TMP_Text Rank;
		/// <summary>
		/// The slider displaying the party member's health.
		/// </summary>
		public Slider Health;

		/// <summary>
		/// Called when the party member button is clicked. Shows dropdown with available actions for the member.
		/// </summary>
		public void Button_OnClick()
		{
			if (UIManager.TryGet("UIDropdown", out UIDropdown uiDropdown) &&
				UIManager.TryGet("UIParty", out UIParty uiParty) &&
				uiParty.Character != null &&
				uiParty.Character.TryGet(out IPartyController partyController) &&
				partyController.ID > 0)
			{
				uiDropdown.Hide();

				ClientNamingSystem.GetCharacterID(Name.text, (id) =>
				{
					if (uiParty.Character.ID == id)
					{
						return;
					}

					uiDropdown.AddButton("Message", () =>
					{
						if (UIManager.TryGet("UIChat", out UIChat uiChat))
						{
							uiChat.SetInputText($"/tell {Name.text} ");
						}
					});

					uiDropdown.AddButton("Add Friend", () =>
					{
						if (uiParty.Character.ID != id)
						{
							Client.Broadcast(new FriendAddNewBroadcast()
							{
								CharacterID = id
							}, Channel.Reliable);
						}
					});

					// Only allow promote/kick if our rank is higher than the target's
					if (Enum.TryParse(Rank.gameObject.name, out PartyRank rank) &&
						rank < partyController.Rank)
					{
						uiDropdown.AddButton("Promote", () =>
						{
							Client.Broadcast(new PartyChangeRankBroadcast()
							{
								MemberID = id,
							}, Channel.Reliable);
						});
						uiDropdown.AddButton("Kick", () =>
						{
							Client.Broadcast(new PartyRemoveBroadcast()
							{
								MemberID = id,
							}, Channel.Reliable);
						});
					}

					uiDropdown.Show();
				});
			}
		}
	}
}
