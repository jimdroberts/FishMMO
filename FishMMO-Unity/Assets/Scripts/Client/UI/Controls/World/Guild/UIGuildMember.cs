using UnityEngine;
using TMPro;
using FishMMO.Shared;
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

			if (UIManager.TryGet("UIDropdown", out UIDropdown uiDropdown))
			{
				uiDropdown.Hide();

				uiDropdown.AddButton("Message", () =>
				{

				});
				uiDropdown.AddButton("Add Friend", () =>
				{

				});

				uiDropdown.Show();
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
					uiDropdown.AddButton("Promote", () =>
					{

					});
					uiDropdown.AddButton("Demote", () =>
					{

					});
					uiDropdown.AddButton("Kick", () =>
					{

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
