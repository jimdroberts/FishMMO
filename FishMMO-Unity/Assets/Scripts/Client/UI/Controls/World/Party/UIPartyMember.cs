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
		public TMP_Text Name;
		public TMP_Text Rank;
		public Slider Health;

		public void Button_OnClick()
		{
			Debug.Log(Name.text);

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
								characterID = id
							}, Channel.Reliable);
						}
					});

					if (Enum.TryParse(Rank.gameObject.name, out PartyRank rank) &&
						rank < partyController.Rank)
					{
						uiDropdown.AddButton("Kick", () =>
						{
							Client.Broadcast(new PartyRemoveBroadcast()
							{
								memberID = id,
							}, Channel.Reliable);
						});
					}

					uiDropdown.Show();
				});
			}
		}
	}
}
