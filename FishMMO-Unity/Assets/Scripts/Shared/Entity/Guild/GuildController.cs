using UnityEngine;
using FishNet.Object;
using System.Collections.Generic;
using System.Linq;
#if !UNITY_SERVER
using FishMMO.Client;
#endif

namespace FishMMO.Shared
{
	/// <summary>
	/// Character guild controller.
	/// </summary>
	[RequireComponent(typeof(Character))]
	public class GuildController : NetworkBehaviour
	{
		public Character Character;

		public long ID;
		public string Name;
		public GuildRank Rank = GuildRank.None;
		public readonly HashSet<long> Officers = new HashSet<long>();
		public readonly HashSet<long> Members = new HashSet<long>();

		public override void OnStartClient()
		{
			base.OnStartClient();

			if (!base.IsOwner)
			{
				enabled = false;
				return;
			}

			ClientManager.RegisterBroadcast<GuildInviteBroadcast>(OnClientGuildInviteBroadcastReceived);
			ClientManager.RegisterBroadcast<GuildAddBroadcast>(OnClientGuildAddBroadcastReceived);
			ClientManager.RegisterBroadcast<GuildLeaveBroadcast>(OnClientGuildLeaveBroadcastReceived);
			ClientManager.RegisterBroadcast<GuildAddMultipleBroadcast>(OnClientGuildAddMultipleBroadcastReceived);
			ClientManager.RegisterBroadcast<GuildRemoveBroadcast>(OnClientGuildRemoveBroadcastReceived);
		}

		public override void OnStopClient()
		{
			base.OnStopClient();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<GuildInviteBroadcast>(OnClientGuildInviteBroadcastReceived);
				ClientManager.UnregisterBroadcast<GuildAddBroadcast>(OnClientGuildAddBroadcastReceived);
				ClientManager.UnregisterBroadcast<GuildLeaveBroadcast>(OnClientGuildLeaveBroadcastReceived);
				ClientManager.UnregisterBroadcast<GuildAddMultipleBroadcast>(OnClientGuildAddMultipleBroadcastReceived);
				ClientManager.UnregisterBroadcast<GuildRemoveBroadcast>(OnClientGuildRemoveBroadcastReceived);
			}
		}

		/// <summary>
		/// When the character receives an invitation to join a guild.
		/// *Note* msg.targetClientID should be our own ClientId but it doesn't matter if it changes. Server has authority.
		/// </summary>
		public void OnClientGuildInviteBroadcastReceived(GuildInviteBroadcast msg)
		{
#if !UNITY_SERVER
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.inviterCharacterID, (n) =>
			{
				if (UIManager.TryGet("UIConfirmationTooltip", out UIConfirmationTooltip uiTooltip))
				{
					uiTooltip.Open("You have been invited to join " + n + "'s guild. Would you like to join?",
					() =>
					{
						ClientManager.Broadcast(new GuildAcceptInviteBroadcast());
					},
					() =>
					{
						ClientManager.Broadcast(new GuildDeclineInviteBroadcast());
					});
				}
			});
#endif
		}

		/// <summary>
		/// When we add a new guild member to the guild.
		/// </summary>
		public void OnClientGuildAddBroadcastReceived(GuildAddBroadcast msg)
		{
			// update our Guild list with the new Guild member
#if !UNITY_SERVER
			if (Character == null || !base.IsOwner)
			{
				return;
			}

			if (!Members.Contains(msg.characterID))
			{
				Members.Add(msg.characterID);
			}

			// are we updating ourself?
			if (msg.characterID == Character.ID)
			{
				ID = msg.guildID;
				Rank = msg.rank;

				ClientNamingSystem.SetName(NamingSystemType.GuildName, msg.guildID, (s) =>
				{
					Character.SetGuildName(s);
				});
			}

			// update the UI
			if (UIManager.TryGet("UIGuild", out UIGuild uiGuild))
			{
				ClientNamingSystem.SetName(NamingSystemType.GuildName, msg.guildID, (s) =>
				{
					if (uiGuild.GuildLabel != null)
					{
						uiGuild.GuildLabel.text = s;
					}
				});
				uiGuild.OnGuildAddMember(msg.characterID, msg.rank, msg.location);
			}
#endif
		}

		/// <summary>
		/// When our local client leaves the guild.
		/// </summary>
		public void OnClientGuildLeaveBroadcastReceived(GuildLeaveBroadcast msg)
		{
#if !UNITY_SERVER
			ID = 0;
			Name = "";
			Rank = GuildRank.None;
			Officers.Clear();
			Members.Clear();

			if (Character != null)
			{
				Character.SetGuildName("");
			}

			if (UIManager.TryGet("UIGuild", out UIGuild uiGuild))
			{
				uiGuild.OnLeaveGuild();
			}
#endif
		}

		/// <summary>
		/// When we need to add guild members.
		/// </summary>
		public void OnClientGuildAddMultipleBroadcastReceived(GuildAddMultipleBroadcast msg)
		{
#if !UNITY_SERVER
			if (!UIManager.TryGet("UIGuild", out UIGuild uiGuild))
			{
				return;
			}

			var newIds = msg.members.Select(x => x.characterID).ToHashSet();
			foreach (long id in new HashSet<long>(Members))
			{
				if (!newIds.Contains(id))
				{
					Members.Remove(id);
					uiGuild.OnGuildRemoveMember(id);
				}
			}
			foreach (GuildAddBroadcast member in msg.members)
			{
				if (!Members.Contains(member.characterID))
				{
					Members.Add(member.characterID);

					// if this is our own id
					if (Character != null && member.characterID == Character.ID)
					{
						ID = member.guildID;
						Rank = member.rank;

						ClientNamingSystem.SetName(NamingSystemType.GuildName, member.guildID, (s) =>
						{
							Character.SetGuildName(s);
							if (uiGuild.GuildLabel != null)
							{
								uiGuild.GuildLabel.text = s;
							}
						});
					}
					// try to add the member to the list
					uiGuild.OnGuildAddMember(member.characterID, member.rank, member.location);
				}
				if (member.rank == GuildRank.Officer &&
					!Officers.Contains(member.characterID))
				{
					Officers.Add(member.characterID);
				}
			}
#endif
		}

		/// <summary>
		/// When we need to remove guild members.
		/// </summary>
		public void OnClientGuildRemoveBroadcastReceived(GuildRemoveBroadcast msg)
		{
#if !UNITY_SERVER
			foreach (long characterID in msg.members)
			{
				if (Members.Contains(characterID))
				{
					Members.Remove(characterID);

					if (UIManager.TryGet("UIGuild", out UIGuild uiGuild))
					{
						uiGuild.OnGuildRemoveMember(characterID);
					}
				}
				Officers.Remove(characterID);
			}
#endif
		}
	}
}