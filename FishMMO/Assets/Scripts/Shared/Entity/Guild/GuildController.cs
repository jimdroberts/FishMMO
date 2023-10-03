using UnityEngine;
using FishNet.Object;
using System.Collections.Generic;
#if !UNITY_SERVER
using FishMMO.Client;
#endif

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

		ClientManager.RegisterBroadcast<GuildCreateBroadcast>(OnClientGuildCreateBroadcastReceived);
		ClientManager.RegisterBroadcast<GuildInviteBroadcast>(OnClientGuildInviteBroadcastReceived);
		ClientManager.RegisterBroadcast<GuildNewMemberBroadcast>(OnClientGuildNewMemberBroadcastReceived);
		ClientManager.RegisterBroadcast<GuildLeaveBroadcast>(OnClientGuildLeaveBroadcastReceived);
		ClientManager.RegisterBroadcast<GuildAddBroadcast>(OnClientGuildAddBroadcastReceived);
		ClientManager.RegisterBroadcast<GuildRemoveBroadcast>(OnClientGuildRemoveBroadcastReceived);
	}

	public override void OnStopClient()
	{
		base.OnStopClient();

		if (base.IsOwner)
		{
			ClientManager.UnregisterBroadcast<GuildCreateBroadcast>(OnClientGuildCreateBroadcastReceived);
			ClientManager.UnregisterBroadcast<GuildInviteBroadcast>(OnClientGuildInviteBroadcastReceived);
			ClientManager.UnregisterBroadcast<GuildNewMemberBroadcast>(OnClientGuildNewMemberBroadcastReceived);
			ClientManager.UnregisterBroadcast<GuildLeaveBroadcast>(OnClientGuildLeaveBroadcastReceived);
			ClientManager.UnregisterBroadcast<GuildAddBroadcast>(OnClientGuildAddBroadcastReceived);
			ClientManager.UnregisterBroadcast<GuildRemoveBroadcast>(OnClientGuildRemoveBroadcastReceived);
		}
	}

	/// <summary>
	/// When the server successfully creates the characters guild.
	/// </summary>
	public void OnClientGuildCreateBroadcastReceived(GuildCreateBroadcast msg)
	{
		ID = msg.ID;
		Name = msg.guildName;
		Rank = GuildRank.Leader;

#if !UNITY_SERVER
		if (UIManager.TryGet("UIGuild", out UIGuild uiGuild))
		{
			uiGuild.OnGuildCreated(msg.location);
		}
#endif
	}

	/// <summary>
	/// When the character receives an invitation to join a guild.
	/// *Note* msg.targetClientID should be our own ClientId but it doesn't matter if it changes. Server has authority.
	/// </summary>
	public void OnClientGuildInviteBroadcastReceived(GuildInviteBroadcast msg)
	{
		ClientManager.Broadcast(new GuildAcceptInviteBroadcast());// instant Guild accept, temp for testing
		//ClientManager.Broadcast(new GuildDeclineInviteBroadcast());// instant decline invite, temp for testing

		// display invitation popup
	}

	/// <summary>
	/// When we add a new guild member to the guild.
	/// </summary>
	public void OnClientGuildNewMemberBroadcastReceived(GuildNewMemberBroadcast msg)
	{
		// update our Guild list with the new Guild member
#if !UNITY_SERVER
		if (UIManager.TryGet("UIGuild", out UIGuild uiGuild))
		{
			uiGuild.OnGuildAddMember(msg.memberID, msg.rank, msg.location);
		}
#endif
	}

	/// <summary>
	/// When a guild members status is updated.
	/// </summary>
	public void OnClientGuildUpdateMemberBroadcastReceived(GuildUpdateMemberBroadcast msg)
	{
#if !UNITY_SERVER
		if (UIManager.TryGet("UIGuild", out UIGuild uiGuild))
		{
			uiGuild.OnGuildAddMember(msg.memberID, msg.rank, msg.location);
		}
#endif
	}

	/// <summary>
	/// When our local client leaves the guild.
	/// </summary>
	public void OnClientGuildLeaveBroadcastReceived(GuildLeaveBroadcast msg)
	{
		ID = 0;
		Name = "";
		Rank = GuildRank.None;
		Officers.Clear();
		Members.Clear();
#if !UNITY_SERVER
		if (UIManager.TryGet("UIGuild", out UIGuild uiGuild))
		{
			uiGuild.OnLeaveGuild();
		}
#endif
	}

	/// <summary>
	/// When we need to add guild members.
	/// </summary>
	public void OnClientGuildAddBroadcastReceived(GuildAddBroadcast msg)
	{
		foreach (GuildNewMemberBroadcast member in msg.members)
		{
			if (!Members.Contains(member.memberID))
			{
				Members.Add(member.memberID);
#if !UNITY_SERVER
				if (UIManager.TryGet("UIGuild", out UIGuild uiGuild))
				{
					uiGuild.OnGuildAddMember(member.memberID, member.rank, member.location);
				}
#endif
			}
			if (member.rank == GuildRank.Officer &&
				!Officers.Contains(member.memberID))
			{
				Officers.Add(member.memberID);
			}
		}
	}

	/// <summary>
	/// When we need to remove guild members.
	/// </summary>
	public void OnClientGuildRemoveBroadcastReceived(GuildRemoveBroadcast msg)
	{
		foreach (long memberID in msg.members)
		{
			if (Members.Contains(memberID))
			{
				Members.Remove(memberID);
#if !UNITY_SERVER
				if (UIManager.TryGet("UIGuild", out UIGuild uiGuild))
				{
					uiGuild.OnGuildRemoveMember(memberID);
				}
#endif
			}
			Officers.Remove(memberID);
		}
	}
}