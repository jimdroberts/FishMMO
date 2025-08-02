using UnityEngine;
using TMPro;
using FishMMO.Shared;
using FishNet.Transporting;
using System;

namespace FishMMO.Client
{
	/// <summary>
	/// UI component representing a guild member, with controls for messaging, friend requests, and rank management.
	/// </summary>
	public class UIGuildMember : MonoBehaviour
	{
		/// <summary>
		/// The name text field for the guild member.
		/// </summary>
		public TMP_Text Name;
		/// <summary>
		/// The rank text field for the guild member.
		/// </summary>
		public TMP_Text Rank;
		/// <summary>
		/// The location text field for the guild member.
		/// </summary>
		public TMP_Text Location;

		/// <summary>
		/// Handles click on the member's name. Shows dropdown for messaging and friend options.
		/// </summary>
		public void Button_OnClickName()
		{
			//Log.Debug(Name.text);

			if (UIManager.TryGet("UIDropdown", out UIDropdown uiDropdown) &&
				UIManager.TryGet("UIGuild", out UIGuild uiGuild) &&
				uiGuild.Character != null)
			{
				uiDropdown.Hide();

				// Get the character ID for the clicked name
				ClientNamingSystem.GetCharacterID(Name.text, (id) =>
				{
					// Prevent actions on self
					if (uiGuild.Character.ID == id)
					{
						return;
					}

					// Add message button to dropdown
					uiDropdown.AddButton("Message", () =>
					{
						if (UIManager.TryGet("UIChat", out UIChat uiChat))
						{
							uiChat.SetInputText($"/tell {Name.text} ");
						}
					});

					// Add friend button to dropdown
					uiDropdown.AddButton("Add Friend", () =>
					{
						if (uiGuild.Character.ID != id)
						{
							Client.Broadcast(new FriendAddNewBroadcast()
							{
								CharacterID = id
							}, Channel.Reliable);
						}
					});

					uiDropdown.Show();
				});
			}
		}

		/// <summary>
		/// Handles click on the member's rank. Shows dropdown for promote, demote, and kick options if allowed.
		/// </summary>
		public void Button_OnClickRank()
		{
			//Log.Debug(Rank.text);

			if (UIManager.TryGet("UIDropdown", out UIDropdown uiDropdown) &&
				UIManager.TryGet("UIGuild", out UIGuild uiGuild) &&
				uiGuild.Character != null &&
				uiGuild.Character.TryGet(out IGuildController guildController) &&
				guildController.ID > 0)
			{
				uiDropdown.Hide();

				// Only allow rank changes if the clicked rank is below the user's rank
				if (Enum.TryParse(Rank.text, out GuildRank rank) &&
					rank < guildController.Rank)
				{
					GuildRank nextRank = rank + 1;
					GuildRank prevRank = rank - 1;
					// Add promote button if next rank is allowed
					if (nextRank < guildController.Rank)
					{
						uiDropdown.AddButton("Promote", () =>
						{
							ClientNamingSystem.GetCharacterID(Name.text, (id) =>
							{
								Client.Broadcast(new GuildChangeRankBroadcast()
								{
									GuildMemberID = id,
									Rank = nextRank,
								}, Channel.Reliable);
							});
						});
					}
					// Add demote button if previous rank is allowed
					if (prevRank > GuildRank.None)
					{
						uiDropdown.AddButton("Demote", () =>
						{
							ClientNamingSystem.GetCharacterID(Name.text, (id) =>
							{
								Client.Broadcast(new GuildChangeRankBroadcast()
								{
									GuildMemberID = id,
									Rank = prevRank,
								}, Channel.Reliable);
							});
						});
					}
					// Add kick button
					uiDropdown.AddButton("Kick", () =>
					{
						ClientNamingSystem.GetCharacterID(Name.text, (id) =>
						{
							Client.Broadcast(new GuildRemoveBroadcast()
							{
								GuildMemberID = id,
							}, Channel.Reliable);
						});
					});
				}

				// Only show dropdown if there are buttons or toggles
				if (uiDropdown.Buttons.Count > 0 ||
					uiDropdown.Toggles.Count > 0)
				{
					uiDropdown.Show();
				}
			}
		}

		/// <summary>
		/// Handles click on the member's location. Currently logs the location text.
		/// </summary>
		public void Button_OnClickLocation()
		{
			//Log.Debug(Location.text);
		}
	}
}
