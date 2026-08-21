using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Character guild controller. Manages guild membership, events, and synchronization for a character.
	/// </summary>
	public class GuildController : CharacterBehaviour, IGuildController
	{
		/// <summary>
		/// Event triggered when a guild invite is received. Parameter: inviter's character ID.
		/// </summary>
		public event Action<long> OnReceiveGuildInvite;

		/// <summary>
		/// Event triggered when a guild member is added, or their roster row changes.
		/// </summary>
		public event Action<GuildAddBroadcast> OnAddGuildMember;

		/// <summary>
		/// Event triggered to validate the set of guild members. Parameter: set of member IDs.
		/// </summary>
		public event Action<HashSet<long>> OnValidateGuildMembers;

		/// <summary>
		/// Event triggered when a guild member is removed. Parameter: member ID.
		/// </summary>
		public event Action<long> OnRemoveGuildMember;

		/// <summary>
		/// Event triggered when leaving a guild.
		/// </summary>
		public event Action OnLeaveGuild;

		/// <summary>
		/// Event triggered when a guild result is received. Parameter: result type.
		/// </summary>
		public event Action<GuildResultType> OnReceiveGuildResult;

		/// <summary>
		/// Event triggered when the guild's descriptive text arrives.
		/// Parameters: guild ID, name, notice, message of the day.
		/// </summary>
		public event Action<long, string, string, string> OnReceiveGuildInfo;

		/// <summary>
		/// Event triggered when the guild's recent activity log arrives.
		/// Parameters: guild ID, entries (newest first).
		/// </summary>
		public event Action<long, GuildLogEntry[]> OnReceiveGuildLog;

		/// <summary>
		/// Event triggered when the guild's rank ladder arrives.
		/// </summary>
		public event Action<GuildRankListBroadcast> OnReceiveGuildRanks;

		/// <summary>
		/// Event triggered when the guild's own recruitment advertisement arrives.
		/// </summary>
		public event Action<GuildRecruitmentInfoBroadcast> OnReceiveGuildRecruitmentInfo;

		/// <summary>
		/// Event triggered when a page of the recruitment directory arrives.
		/// </summary>
		public event Action<GuildDirectoryEntry[]> OnReceiveGuildDirectory;

		/// <summary>
		/// Event triggered when the guild's pending application queue arrives.
		/// </summary>
		public event Action<GuildApplicationEntry[]> OnReceiveGuildApplications;

		[Header("ECA - Guild")]
		[Tooltip("Triggers invoked when the character joins a guild.")]
		[SerializeField]
		private List<Trigger> onGuildJoinTriggers = new List<Trigger>();
		[Tooltip("Triggers invoked when the character leaves a guild.")]
		[SerializeField]
		private List<Trigger> onGuildLeaveTriggers = new List<Trigger>();

		/// <inheritdoc />
		public List<Trigger> OnGuildJoinTriggers => onGuildJoinTriggers;
		/// <inheritdoc />
		public List<Trigger> OnGuildLeaveTriggers => onGuildLeaveTriggers;

		/// <summary>
		/// The unique guild ID for this character. Synchronized over the network.
		/// </summary>
		public long ID { get { return GID.Value; } set { GID.Value = value; } }

		/// <summary>
		/// The character's position on the guild's rank ladder. Zero means "not in a guild".
		/// </summary>
		public byte RankOrder { get; set; }

		/// <summary>
		/// The permissions the character's rank holds, as computed by the server.
		/// </summary>
		public GuildPermissions Permissions { get; set; }

		/// <summary>
		/// The highest rank order that exists in this guild — the leader's seat.
		/// </summary>
		public byte LeaderRankOrder { get; set; }

		/// <inheritdoc />
		/// <remarks>
		/// <c>HasFlag</c> is avoided deliberately: it boxes on the enum and this is called once
		/// per row per roster refresh, which for a full guild is a hundred allocations for a
		/// bitwise AND.
		/// </remarks>
		public bool HasGuildPermission(GuildPermissions permission)
		{
			return (Permissions & permission) == permission;
		}

		/// <summary>
		/// Clears every cached component of the character's guild standing.
		/// </summary>
		/// <remarks>
		/// One method because forgetting one of the three is a real defect and not a visible one:
		/// a stale <see cref="Permissions"/> mask left behind after leaving a guild would draw a
		/// disband button for a player with no guild, and the refusal would arrive from the server
		/// with no explanation the panel could render.
		/// </remarks>
		private void ClearGuildStanding()
		{
			RankOrder = 0;
			Permissions = GuildPermissions.None;
			LeaderRankOrder = 0;
		}

		/// <summary>
		/// SyncVar for the guild ID, used for network synchronization. Configured for unreliable channel and server-only writes.
		/// </summary>
		private readonly SyncVar<long> GID = new SyncVar<long>(0, new SyncTypeSettings()
		{
			SendRate = 1.0f,
			Channel = Channel.Unreliable,
			ReadPermission = ReadPermission.ExcludeOwner,
			WritePermission = WritePermission.ServerOnly,
		});

#if !UNITY_SERVER
		/// <summary>
		/// Called when the object is awakened. Subscribes to guild ID changes.
		/// </summary>
		public override void OnAwake()
		{
			base.OnAwake();

			GID.OnChange += OnGuildIDChanged;
		}

		/// <summary>
		/// Called when the object is being destroyed. Unsubscribes from guild ID changes.
		/// </summary>
		public override void OnDestroying()
		{
			base.OnDestroying();

			GID.OnChange -= OnGuildIDChanged;
		}

		/// <summary>
		/// Callback invoked when the guild ID SyncVar changes. Resets rank if the character leaves a guild.
		/// </summary>
		/// <param name="prev">The previous guild ID.</param>
		/// <param name="next">The new guild ID.</param>
		/// <param name="asServer">Whether this callback is executing on the server.</param>
		private void OnGuildIDChanged(long prev, long next, bool asServer)
		{
			if (next == 0)
			{
				ClearGuildStanding();
			}
			IGuildController.OnReadID?.Invoke(next, PlayerCharacter);
		}

		/// <summary>
		/// Called when the character starts. Registers guild broadcast handlers on the owning client.
		/// </summary>
		public override void OnStartCharacter()
		{
			base.OnStartCharacter();

			if (base.IsOwner)
			{
				ClientManager.RegisterBroadcast<GuildInviteBroadcast>(OnClientGuildInviteBroadcastReceived);
				ClientManager.RegisterBroadcast<GuildAddBroadcast>(OnClientGuildAddBroadcastReceived);
				ClientManager.RegisterBroadcast<GuildAddMultipleBroadcast>(OnClientGuildAddMultipleBroadcastReceived);
				ClientManager.RegisterBroadcast<GuildLeaveBroadcast>(OnClientGuildLeaveBroadcastReceived);
				ClientManager.RegisterBroadcast<GuildRemoveBroadcast>(OnClientGuildRemoveBroadcastReceived);
				ClientManager.RegisterBroadcast<GuildResultBroadcast>(OnClientGuildResultBroadcastReceived);
				ClientManager.RegisterBroadcast<GuildInfoBroadcast>(OnClientGuildInfoBroadcastReceived);
				ClientManager.RegisterBroadcast<GuildLogBroadcast>(OnClientGuildLogBroadcastReceived);
				ClientManager.RegisterBroadcast<GuildRankListBroadcast>(OnClientGuildRankListBroadcastReceived);
				ClientManager.RegisterBroadcast<GuildRecruitmentInfoBroadcast>(OnClientGuildRecruitmentInfoBroadcastReceived);
				ClientManager.RegisterBroadcast<GuildDirectoryBroadcast>(OnClientGuildDirectoryBroadcastReceived);
				ClientManager.RegisterBroadcast<GuildApplicationListBroadcast>(OnClientGuildApplicationListBroadcastReceived);

				if (PlayerCharacter != null)
				{
					IGuildController.OnReadID?.Invoke(ID, PlayerCharacter);
				}
			}
		}

		/// <summary>
		/// Called when the character stops. Unregisters guild broadcast handlers on the owning client.
		/// </summary>
		public override void OnStopCharacter()
		{
			base.OnStopCharacter();

			if (base.IsOwner)
			{
				ClientManager.UnregisterBroadcast<GuildInviteBroadcast>(OnClientGuildInviteBroadcastReceived);
				ClientManager.UnregisterBroadcast<GuildAddBroadcast>(OnClientGuildAddBroadcastReceived);
				ClientManager.UnregisterBroadcast<GuildAddMultipleBroadcast>(OnClientGuildAddMultipleBroadcastReceived);
				ClientManager.UnregisterBroadcast<GuildLeaveBroadcast>(OnClientGuildLeaveBroadcastReceived);
				ClientManager.UnregisterBroadcast<GuildRemoveBroadcast>(OnClientGuildRemoveBroadcastReceived);
				ClientManager.UnregisterBroadcast<GuildResultBroadcast>(OnClientGuildResultBroadcastReceived);
				ClientManager.UnregisterBroadcast<GuildInfoBroadcast>(OnClientGuildInfoBroadcastReceived);
				ClientManager.UnregisterBroadcast<GuildLogBroadcast>(OnClientGuildLogBroadcastReceived);
				ClientManager.UnregisterBroadcast<GuildRankListBroadcast>(OnClientGuildRankListBroadcastReceived);
				ClientManager.UnregisterBroadcast<GuildRecruitmentInfoBroadcast>(OnClientGuildRecruitmentInfoBroadcastReceived);
				ClientManager.UnregisterBroadcast<GuildDirectoryBroadcast>(OnClientGuildDirectoryBroadcastReceived);
				ClientManager.UnregisterBroadcast<GuildApplicationListBroadcast>(OnClientGuildApplicationListBroadcastReceived);
			}
		}

		/// <summary>
		/// When the character receives an invitation to join a guild.
		/// *Note* msg.targetClientID should be our own ClientId but it doesn't matter if it changes. Server has authority.
		/// </summary>
		public void OnClientGuildInviteBroadcastReceived(GuildInviteBroadcast msg, Channel channel)
		{
			OnReceiveGuildInvite?.Invoke(msg.InviterCharacterID);
		}

		/// <summary>
		/// When we add a new guild member to the guild.
		/// </summary>
		public void OnClientGuildAddBroadcastReceived(GuildAddBroadcast msg, Channel channel)
		{
			// if this is our own id
			if (PlayerCharacter != null && msg.CharacterID == Character.ID)
			{
				ID = msg.GuildID;

				/* The ladder position, stored as sent. It used to be cast to a GuildRank enum
				 * here; ranks are rows a guild owns now, so there is nothing to cast it TO. The
				 * permission mask that decides what this player may do arrives separately, in
				 * GuildRankListBroadcast, because the server computes it rather than letting the
				 * client infer it from a number. */
				RankOrder = msg.RankOrder;

				IGuildController.OnReadID?.Invoke(ID, PlayerCharacter);
				Character.Invoke(onGuildJoinTriggers, new GuildEventData(Character, ID, RankOrder, Permissions));
			}

			// update our Guild list with the new Guild member
			OnAddGuildMember?.Invoke(msg);
		}

		/// <summary>
		/// When we need to add guild members.
		/// </summary>
		public void OnClientGuildAddMultipleBroadcastReceived(GuildAddMultipleBroadcast msg, Channel channel)
		{
			HashSet<long> newIds = new HashSet<long>(msg.Members.Length);
			for (int i = 0; i < msg.Members.Length; i++)
			{
				newIds.Add(msg.Members[i].CharacterID);
			}

			OnValidateGuildMembers?.Invoke(newIds);

			foreach (GuildAddBroadcast subMsg in msg.Members)
			{
				OnClientGuildAddBroadcastReceived(subMsg, channel);
			}
		}

		/// <summary>
		/// When our local client leaves the guild.
		/// </summary>
		public void OnClientGuildLeaveBroadcastReceived(GuildLeaveBroadcast msg, Channel channel)
		{
			if (PlayerCharacter == null)
			{
				return;
			}
			ID = 0;
			ClearGuildStanding();
			OnLeaveGuild?.Invoke();
			Character.Invoke(onGuildLeaveTriggers, new GuildEventData(Character, 0, 0, GuildPermissions.None));
		}

		/// <summary>
		/// When we need to remove guild members.
		/// </summary>
		public void OnClientGuildRemoveBroadcastReceived(GuildRemoveBroadcast msg, Channel channel)
		{
			OnRemoveGuildMember?.Invoke(msg.CharacterID);
		}

		/// <summary>
		/// Handles a guild result broadcast from the server (e.g., creation success, name taken).
		/// </summary>
		/// <param name="msg">The guild result broadcast message.</param>
		/// <param name="channel">The network channel the broadcast was received on.</param>
		public void OnClientGuildResultBroadcastReceived(GuildResultBroadcast msg, Channel channel)
		{
			OnReceiveGuildResult?.Invoke(msg.Result);
		}

		/// <summary>
		/// Handles the guild information broadcast (name, notice, message of the day).
		/// </summary>
		/// <param name="msg">The guild information broadcast message.</param>
		/// <param name="channel">The network channel the broadcast was received on.</param>
		/// <remarks>
		/// Delivered on join and again whenever a leader or officer edits the text, so the panel
		/// never has to ask for it. Not filtered by <see cref="ID"/> here: the server only sends
		/// this to members of the guild it describes, and a client that filtered on its own
		/// (possibly not-yet-updated) guild ID would drop the copy that arrives alongside the join.
		/// </remarks>
		public void OnClientGuildInfoBroadcastReceived(GuildInfoBroadcast msg, Channel channel)
		{
			OnReceiveGuildInfo?.Invoke(msg.GuildID, msg.Name, msg.Notice, msg.MessageOfTheDay);
		}

		/// <summary>
		/// Handles the guild activity log broadcast.
		/// </summary>
		/// <param name="msg">The guild log broadcast message.</param>
		/// <param name="channel">The network channel the broadcast was received on.</param>
		public void OnClientGuildLogBroadcastReceived(GuildLogBroadcast msg, Channel channel)
		{
			OnReceiveGuildLog?.Invoke(msg.GuildID, msg.Entries ?? Array.Empty<GuildLogEntry>());
		}
		/// <summary>
		/// Handles the guild rank ladder broadcast.
		/// </summary>
		/// <param name="msg">The rank list broadcast message.</param>
		/// <param name="channel">The network channel the broadcast was received on.</param>
		/// <remarks>
		/// This message, not <see cref="GuildAddBroadcast"/>, is what establishes what the local
		/// player may DO. The server sends the viewer's own mask inside it rather than expecting
		/// the client to look its rank up in the ladder, so that the panel and the server always
		/// agree about which buttons should exist.
		/// </remarks>
		public void OnClientGuildRankListBroadcastReceived(GuildRankListBroadcast msg, Channel channel)
		{
			RankOrder = msg.ViewerRankOrder;
			Permissions = (GuildPermissions)msg.ViewerPermissions;
			LeaderRankOrder = msg.LeaderRankOrder;

			OnReceiveGuildRanks?.Invoke(msg);
		}

		/// <summary>
		/// Handles the guild's own recruitment advertisement broadcast.
		/// </summary>
		/// <param name="msg">The recruitment info broadcast message.</param>
		/// <param name="channel">The network channel the broadcast was received on.</param>
		public void OnClientGuildRecruitmentInfoBroadcastReceived(GuildRecruitmentInfoBroadcast msg, Channel channel)
		{
			OnReceiveGuildRecruitmentInfo?.Invoke(msg);
		}

		/// <summary>
		/// Handles a page of the recruitment directory.
		/// </summary>
		/// <param name="msg">The directory broadcast message.</param>
		/// <param name="channel">The network channel the broadcast was received on.</param>
		public void OnClientGuildDirectoryBroadcastReceived(GuildDirectoryBroadcast msg, Channel channel)
		{
			OnReceiveGuildDirectory?.Invoke(msg.Entries ?? Array.Empty<GuildDirectoryEntry>());
		}

		/// <summary>
		/// Handles the guild's pending application queue.
		/// </summary>
		/// <param name="msg">The application list broadcast message.</param>
		/// <param name="channel">The network channel the broadcast was received on.</param>
		/// <remarks>
		/// Only ever delivered to a client whose rank holds <c>ManageApplications</c> — the server
		/// does not send the queue to anybody else, so there is nothing to filter here.
		/// </remarks>
		public void OnClientGuildApplicationListBroadcastReceived(GuildApplicationListBroadcast msg, Channel channel)
		{
			OnReceiveGuildApplications?.Invoke(msg.Entries ?? Array.Empty<GuildApplicationEntry>());
		}
#endif

	}
}