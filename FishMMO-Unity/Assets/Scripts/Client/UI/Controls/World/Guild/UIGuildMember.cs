using UnityEngine;
using TMPro;
using FishMMO.Shared;
using FishNet.Transporting;
using System;

namespace FishMMO.Client
{
	public class UIGuildMember : MonoBehaviour
	{
		public TMP_Text Name;
		public TMP_Text Rank;
		public TMP_Text Location;

		public void Button_OnClickName()
		{
			//Debug.Log(Name.text);

			if (UIManager.TryGet("UIDropdown", out UIDropdown uiDropdown) &&
			    UIManager.TryGet("UIGuild", out UIGuild uiGuild) &&
			    uiGuild.Character != null)
			{
				uiDropdown.Hide();

				ClientNamingSystem.GetCharacterID(Name.text, (id) =>
				{
					if (uiGuild.Character.ID == id)
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
						if (uiGuild.Character.ID != id)
						{
							Client.Broadcast(new FriendAddNewBroadcast()
							{
								characterID = id
							}, Channel.Reliable);
						}
					});

					uiDropdown.Show();
				});
			}
		}

		public void Button_OnClickRank()
		{
			//Debug.Log(Rank.text);

			if (UIManager.TryGet("UIDropdown", out UIDropdown uiDropdown) &&
			    UIManager.TryGet("UIGuild", out UIGuild uiGuild) &&
			    uiGuild.Character != null &&
			    uiGuild.Character.TryGet(out IGuildController guildController) &&
			    guildController.ID > 0)
			{
				uiDropdown.Hide();
				
				if (Enum.TryParse(Rank.text, out GuildRank rank) &&
				    rank < guildController.Rank)
				{
					if (rank + 1 < guildController.Rank)
					{
						uiDropdown.AddButton("Promote", () =>
						{

						});
					}
					uiDropdown.AddButton("Demote", () =>
					{

					});
					uiDropdown.AddButton("Kick", () =>
					{
						ClientNamingSystem.GetCharacterID(Name.text, (id) =>
						{
							Client.Broadcast(new GuildRemoveBroadcast()
							{
								guildMemberID = id,
							}, Channel.Reliable);
						});
					});
				}

				if (uiDropdown.Buttons.Count > 0 ||
				    uiDropdown.Toggles.Count > 0)
				{
					uiDropdown.Show();
				}
			}
		}

		public void Button_OnClickLocation()
		{
			//Debug.Log(Location.text);
		}
	}
}
