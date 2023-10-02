using UnityEngine;
using FishNet.Object;
using System;
using System.Collections.Generic;

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

	public event Action OnGuildCreated;
	public event Action OnLeaveGuild;

	public event Action<long, GuildRank, string> OnAddMember;
	public event Action<long, GuildRank> OnUpdateMember;
	public event Action<long> OnRemoveMember;

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
		OnGuildCreated?.Invoke();
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
		OnAddMember?.Invoke(msg.memberID, msg.rank, msg.location);
	}

	/// <summary>
	/// When a guild members status is updated.
	/// </summary>
	public void OnClientGuildUpdateMemberBroadcastReceived(GuildUpdateMemberBroadcast msg)
	{
		OnUpdateMember?.Invoke(msg.memberID, msg.rank);
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
		OnLeaveGuild?.Invoke();
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
				OnAddMember?.Invoke(member.memberID, member.rank, member.location);
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
				OnRemoveMember?.Invoke(memberID);
			}
			Officers.Remove(memberID);
		}
	}
}