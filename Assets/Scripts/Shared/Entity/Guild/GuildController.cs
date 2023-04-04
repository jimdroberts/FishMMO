using UnityEngine;
using FishNet.Object;
using System.Collections.Generic;

/// <summary>
/// Character guild controller.
/// </summary>
[RequireComponent(typeof(Character))]
public class GuildController : NetworkBehaviour
{
	public Character character;

	public GuildRank rank = GuildRank.None;
	public Guild current;

	public delegate void GuildEvent();
	public event GuildEvent OnGuildCreated;
	public event GuildEvent OnLeaveGuild;

	public delegate void GuildMemberEvent(int guildMemberId, GuildRank rank);
	public event GuildMemberEvent OnAddMember;
	public event GuildMemberEvent OnRemoveMember;

	public delegate void GuildAcceptEvent(List<int> guildMemberIds);
	public event GuildAcceptEvent OnGuildInviteAccepted;

	public override void OnStartClient()
	{
		base.OnStartClient();

		if (character == null || !base.IsOwner)
		{
			enabled = false;
			return;
		}

		ClientManager.RegisterBroadcast<GuildCreateBroadcast>(OnClientGuildCreateBroadcastReceived);
		ClientManager.RegisterBroadcast<GuildInviteBroadcast>(OnClientGuildInviteBroadcastReceived);
		ClientManager.RegisterBroadcast<GuildJoinedBroadcast>(OnClientGuildJoinedBroadcastReceived);
		ClientManager.RegisterBroadcast<GuildNewMemberBroadcast>(OnClientGuildNewMemberBroadcastReceived);
		ClientManager.RegisterBroadcast<GuildLeaveBroadcast>(OnClientGuildLeaveBroadcastReceived);
		ClientManager.RegisterBroadcast<GuildRemoveBroadcast>(OnClientGuildRemoveBroadcastReceived);
	}

	public override void OnStopClient()
	{
		base.OnStopClient();

		if (base.IsOwner)
		{
			ClientManager.UnregisterBroadcast<GuildCreateBroadcast>(OnClientGuildCreateBroadcastReceived);
			ClientManager.UnregisterBroadcast<GuildInviteBroadcast>(OnClientGuildInviteBroadcastReceived);
			ClientManager.UnregisterBroadcast<GuildJoinedBroadcast>(OnClientGuildJoinedBroadcastReceived);
			ClientManager.UnregisterBroadcast<GuildNewMemberBroadcast>(OnClientGuildNewMemberBroadcastReceived);
			ClientManager.UnregisterBroadcast<GuildLeaveBroadcast>(OnClientGuildLeaveBroadcastReceived);
			ClientManager.UnregisterBroadcast<GuildRemoveBroadcast>(OnClientGuildRemoveBroadcastReceived);
		}
	}

	/// <summary>
	/// When the server successfully creates the characters guild.
	/// </summary>
	public void OnClientGuildCreateBroadcastReceived(GuildCreateBroadcast msg)
	{
		Guild newGuild = new Guild(msg.guildId, this);
		current = newGuild;
		rank = GuildRank.Leader;

		OnGuildCreated?.Invoke();
	}

	/// <summary>
	/// When the character receives an invitation to join a guild.
	/// *Note* msg.targetClientId should be our own ClientId but it doesn't matter if it changes. Server has authority.
	/// </summary>
	public void OnClientGuildInviteBroadcastReceived(GuildInviteBroadcast msg)
	{
		ClientManager.Broadcast(new GuildAcceptInviteBroadcast());// instant Guild accept, temp for testing
		//ClientManager.Broadcast(new GuildDeclineInviteBroadcast());// instant decline invite, temp for testing

		// display invitation popup
	}

	/// <summary>
	/// When the character successfully joins a guild.
	/// </summary>
	public void OnClientGuildJoinedBroadcastReceived(GuildJoinedBroadcast msg)
	{
		// update our guild list with the guild!
		OnGuildInviteAccepted?.Invoke(msg.members);
	}

	/// <summary>
	/// When we add a new guild member to the guild.
	/// </summary>
	public void OnClientGuildNewMemberBroadcastReceived(GuildNewMemberBroadcast msg)
	{
		// update our Guild list with the new Guild member
		OnAddMember?.Invoke(msg.newMemberClientId, msg.rank);
	}

	/// <summary>
	/// When our local client leaves the guild.
	/// </summary>
	public void OnClientGuildLeaveBroadcastReceived(GuildLeaveBroadcast msg)
	{
		rank = GuildRank.None;
		current = null;

		OnLeaveGuild?.Invoke();
	}

	/// <summary>
	/// When we need to remove a guild member.
	/// </summary>
	public void OnClientGuildRemoveBroadcastReceived(GuildRemoveBroadcast msg)
	{
		if (current != null)
		{
			GuildController removedMember = current.RemoveMember(msg.memberId);
			OnRemoveMember?.Invoke(msg.memberId, GuildRank.None);
		}
	}
}