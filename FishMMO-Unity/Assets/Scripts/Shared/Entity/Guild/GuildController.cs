using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using System.Collections.Generic;
using System.Linq;
#if !UNITY_SERVER
using FishMMO.Client;
using static FishMMO.Client.Client;
#endif

namespace FishMMO.Shared
{
	/// <summary>
	/// Character guild controller.
	/// </summary>
	public class GuildController : CharacterBehaviour
	{
		public long ID;

		public GuildRank Rank = GuildRank.None;

		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			ID = reader.ReadInt64();

#if !UNITY_SERVER
			if (ID != 0)
			{
				// load the characters guild from disk or request it from the server
				ClientNamingSystem.SetName(NamingSystemType.GuildName, ID, (s) =>
				{
					Character.SetGuildName(s);
				});
			}
#endif
		}

		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			writer.WriteInt64(ID);
		}

#if !UNITY_SERVER
		public override void OnStartClient()
		{
			base.OnStartClient();

			if (base.IsOwner)
			{
				ClientManager.RegisterBroadcast<GuildInviteBroadcast>(OnClientGuildInviteBroadcastReceived);
				ClientManager.RegisterBroadcast<GuildAddBroadcast>(OnClientGuildAddBroadcastReceived);
				ClientManager.RegisterBroadcast<GuildLeaveBroadcast>(OnClientGuildLeaveBroadcastReceived);
				ClientManager.RegisterBroadcast<GuildAddMultipleBroadcast>(OnClientGuildAddMultipleBroadcastReceived);
				ClientManager.RegisterBroadcast<GuildRemoveBroadcast>(OnClientGuildRemoveBroadcastReceived);
			}
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
		public void OnClientGuildInviteBroadcastReceived(GuildInviteBroadcast msg, Channel channel)
		{
			ClientNamingSystem.SetName(NamingSystemType.CharacterName, msg.inviterCharacterID, (n) =>
			{
				if (UIManager.TryGet("UIConfirmationTooltip", out UIConfirmationTooltip uiTooltip))
				{
					uiTooltip.Open("You have been invited to join " + n + "'s guild. Would you like to join?",
					() =>
					{
						Broadcast(new GuildAcceptInviteBroadcast(), Channel.Reliable);
					},
					() =>
					{
						Broadcast(new GuildDeclineInviteBroadcast(), Channel.Reliable);
					});
				}
			});
		}

		/// <summary>
		/// When we add a new guild member to the guild.
		/// </summary>
		public void OnClientGuildAddBroadcastReceived(GuildAddBroadcast msg, Channel channel)
		{
			// update our Guild list with the new Guild member
			if (Character == null)
			{
				return;
			}

			if (!UIManager.TryGet("UIGuild", out UIGuild uiGuild))
			{
				return;
			}

			uiGuild.OnGuildAddMember(msg.characterID, msg.rank, msg.location);
			ClientNamingSystem.SetName(NamingSystemType.GuildName, msg.guildID, (s) =>
			{
				if (uiGuild.GuildLabel != null)
				{
					uiGuild.GuildLabel.text = s;
				}
			});
		}

		/// <summary>
		/// When our local client leaves the guild.
		/// </summary>
		public void OnClientGuildLeaveBroadcastReceived(GuildLeaveBroadcast msg, Channel channel)
		{
			if (UIManager.TryGet("UIGuild", out UIGuild uiGuild))
			{
				uiGuild.OnLeaveGuild();
			}
		}

		/// <summary>
		/// When we need to add guild members.
		/// </summary>
		public void OnClientGuildAddMultipleBroadcastReceived(GuildAddMultipleBroadcast msg, Channel channel)
		{
			if (!UIManager.TryGet("UIGuild", out UIGuild uiGuild))
			{
				return;
			}

			var newIds = msg.members.Select(x => x.characterID).ToHashSet();
			foreach (long id in new HashSet<long>(uiGuild.Members.Keys))
			{
				if (!newIds.Contains(id))
				{
					uiGuild.OnGuildRemoveMember(id);
				}
			}
			foreach (GuildAddBroadcast subMsg in msg.members)
			{
				OnClientGuildAddBroadcastReceived(subMsg, channel);
			}
		}

		/// <summary>
		/// When we need to remove guild members.
		/// </summary>
		public void OnClientGuildRemoveBroadcastReceived(GuildRemoveBroadcast msg, Channel channel)
		{
			if (UIManager.TryGet("UIGuild", out UIGuild uiGuild))
			{
				foreach (long characterID in msg.members)
				{
					uiGuild.OnGuildRemoveMember(characterID);
				}
			}
		}
#endif
	}
}