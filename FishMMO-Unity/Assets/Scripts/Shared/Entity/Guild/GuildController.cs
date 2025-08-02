using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.Linq;

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
		/// Event triggered when a guild member is added. Parameters: character ID, guild ID, rank, location.
		/// </summary>
		public event Action<long, long, GuildRank, string> OnAddGuildMember;

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
		/// The unique guild ID for this character. Synchronized over the network.
		/// </summary>
		public long ID { get { return GID.Value; } set { GID.Value = value; } }

		/// <summary>
		/// The rank of the character in the guild (e.g., Member, Leader).
		/// </summary>
		public GuildRank Rank { get; set; }

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

		public override void OnDestroying()
		{
			base.OnDestroying();

			GID.OnChange -= OnGuildIDChanged;
		}

		private void OnGuildIDChanged(long prev, long next, bool asServer)
		{
			if (next == 0)
			{
				Rank = GuildRank.None;
			}
			IGuildController.OnReadID?.Invoke(next, PlayerCharacter);
		}

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

				if (PlayerCharacter != null)
				{
					IGuildController.OnReadID?.Invoke(ID, PlayerCharacter);
				}
			}
		}

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
				Rank = msg.Rank;

				IGuildController.OnReadID?.Invoke(ID, PlayerCharacter);
			}

			// update our Guild list with the new Guild member
			OnAddGuildMember?.Invoke(msg.CharacterID, msg.GuildID, msg.Rank, msg.Location);
		}

		/// <summary>
		/// When we need to add guild members.
		/// </summary>
		public void OnClientGuildAddMultipleBroadcastReceived(GuildAddMultipleBroadcast msg, Channel channel)
		{
			var newIds = msg.Members.Select(x => x.CharacterID).ToHashSet();

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
			OnLeaveGuild?.Invoke();
		}

		/// <summary>
		/// When we need to remove guild members.
		/// </summary>
		public void OnClientGuildRemoveBroadcastReceived(GuildRemoveBroadcast msg, Channel channel)
		{
			OnRemoveGuildMember?.Invoke(msg.GuildMemberID);
		}

		public void OnClientGuildResultBroadcastReceived(GuildResultBroadcast msg, Channel channel)
		{
			OnReceiveGuildResult?.Invoke(msg.Result);
		}
#endif
	}
}