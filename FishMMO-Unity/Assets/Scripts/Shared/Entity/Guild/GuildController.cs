using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FishMMO.Shared
{
	/// <summary>
	/// Character guild controller.
	/// </summary>
	public class GuildController : CharacterBehaviour
	{
		public long ID;
		public GuildRank Rank = GuildRank.None;

		public Action<long> OnReadPayload;
		public Action<long> OnReceiveGuildInvite;
		public Action<long, long, GuildRank, string> OnAddGuildMember;
		public Action<HashSet<long>> OnValidateGuildMembers;
		public Action<long> OnRemoveGuildMember;
		public Action OnLeaveGuild;

		public override void ReadPayload(NetworkConnection connection, Reader reader)
		{
			ID = reader.ReadInt64();

			OnReadPayload?.Invoke(ID);
		}

		public override void WritePayload(NetworkConnection connection, Writer writer)
		{
			writer.WriteInt64(ID);
		}

#if !UNITY_SERVER
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (base.IsOwner)
			{
				ClientManager.RegisterBroadcast<GuildInviteBroadcast>(OnClientGuildInviteBroadcastReceived);
				ClientManager.RegisterBroadcast<GuildAddBroadcast>(OnClientGuildAddBroadcastReceived);
				ClientManager.RegisterBroadcast<GuildLeaveBroadcast>(OnClientGuildLeaveBroadcastReceived);
				ClientManager.RegisterBroadcast<GuildAddMultipleBroadcast>(OnClientGuildAddMultipleBroadcastReceived);
				ClientManager.RegisterBroadcast<GuildRemoveBroadcast>(OnClientGuildRemoveBroadcastReceived);

				OnReadPayload?.Invoke(ID);
			}
		}

		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

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
			OnReceiveGuildInvite?.Invoke(msg.inviterCharacterID);
		}

		/// <summary>
		/// When we add a new guild member to the guild.
		/// </summary>
		public void OnClientGuildAddBroadcastReceived(GuildAddBroadcast msg, Channel channel)
		{
			// if this is our own id
			if (Character != null && msg.characterID == Character.ID)
			{
				ID = msg.guildID;
				Rank = msg.rank;

				OnReadPayload?.Invoke(ID);
			}

			// update our Guild list with the new Guild member
			OnAddGuildMember?.Invoke(msg.characterID, msg.guildID, msg.rank, msg.location);
		}

		/// <summary>
		/// When our local client leaves the guild.
		/// </summary>
		public void OnClientGuildLeaveBroadcastReceived(GuildLeaveBroadcast msg, Channel channel)
		{
			Character.SetGuildName(null);
			ID = 0;
			Rank = GuildRank.None;
			OnLeaveGuild?.Invoke();
		}

		/// <summary>
		/// When we need to add guild members.
		/// </summary>
		public void OnClientGuildAddMultipleBroadcastReceived(GuildAddMultipleBroadcast msg, Channel channel)
		{
			var newIds = msg.members.Select(x => x.characterID).ToHashSet();

			OnValidateGuildMembers?.Invoke(newIds);

			foreach (GuildAddBroadcast subMsg in msg.members)
			{
				OnAddGuildMember?.Invoke(subMsg.characterID, subMsg.guildID, subMsg.rank, subMsg.location);
			}
		}

		/// <summary>
		/// When we need to remove guild members.
		/// </summary>
		public void OnClientGuildRemoveBroadcastReceived(GuildRemoveBroadcast msg, Channel channel)
		{
			foreach (long characterID in msg.members)
			{
				OnRemoveGuildMember?.Invoke(characterID);
			}
		}
#endif
	}
}