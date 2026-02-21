using System;
using System.Collections.Concurrent;
using FishMMO.Server.Core.Collections;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for guild system state.
	/// Manages guild invitations and database synchronization state separately from GuildSystem logic.
	/// </summary>
	public class GuildSystemRuntimeData : RuntimeDataContainer, IGuildSystemRuntimeData
	{
		/// <summary>
		/// Tracks pending guild invitations using a last-seen queue for O(1) touch and bounded TTL sweep.
		/// </summary>
		private LastSeenCacheTracker<long, long> pendingInvitations;

		/// <summary>
		/// Timestamp of the last successful database fetch for guild updates.
		/// </summary>
		public DateTime LastFetchTime { get; set; }

		/// <inheritdoc/>
		public int UpdatePumpInFlight { get; set; }

		/// <inheritdoc/>
		public DateTime NextInvitationSweepUtc { get; set; }

		/// <inheritdoc/>
		public ConcurrentDictionary<long, DateTime> NextAllowedIngressUtcByKey { get; private set; }

		/// <inheritdoc/>
		public ConcurrentDictionary<long, byte> IngressInFlightByKey { get; private set; }

		/// <inheritdoc/>
		public DateTime NextIngressSweepUtc { get; set; }

		/// <summary>
		/// Initializes the guild runtime data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			pendingInvitations = new LastSeenCacheTracker<long, long>();
			LastFetchTime = DateTime.UtcNow;
			UpdatePumpInFlight = 0;
			NextInvitationSweepUtc = DateTime.UtcNow;
			NextAllowedIngressUtcByKey = new ConcurrentDictionary<long, DateTime>();
			IngressInFlightByKey = new ConcurrentDictionary<long, byte>();
			NextIngressSweepUtc = DateTime.UtcNow;
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all guild runtime data.
		/// </summary>
		public override void Clear()
		{
			pendingInvitations?.Clear();
			LastFetchTime = DateTime.UtcNow;
			UpdatePumpInFlight = 0;
			NextInvitationSweepUtc = DateTime.UtcNow;
			NextAllowedIngressUtcByKey?.Clear();
			IngressInFlightByKey?.Clear();
			NextIngressSweepUtc = DateTime.UtcNow;
		}

		/// <inheritdoc/>
		public bool TryGetPendingInvitation(long targetCharacterID, out long guildID)
		{
			if (pendingInvitations == null)
			{
				guildID = 0;
				return false;
			}

			return pendingInvitations.TryGetAndTouch(targetCharacterID, DateTime.UtcNow, out guildID);
		}

		/// <inheritdoc/>
		public bool TryAddPendingInvitation(long targetCharacterID, long guildID, DateTime nowUtc)
		{
			if (pendingInvitations == null)
			{
				return false;
			}

			if (pendingInvitations.TryGetAndTouch(targetCharacterID, nowUtc, out _))
			{
				return false;
			}

			pendingInvitations.Upsert(targetCharacterID, guildID, nowUtc);
			return true;
		}

		/// <inheritdoc/>
		public bool RemovePendingInvitation(long targetCharacterID)
		{
			pendingInvitations?.Remove(targetCharacterID);
			return true;
		}

		/// <inheritdoc/>
		public int SweepExpiredInvitations(DateTime nowUtc, TimeSpan ttl, int maxScan, int maxRemove)
		{
			if (pendingInvitations == null)
			{
				return 0;
			}

			return pendingInvitations.SweepExpired(nowUtc, ttl, maxScan, maxRemove);
		}

		/// <summary>
		/// Deinitializes the guild runtime data container.
		/// </summary>
		public override void Deinitialize()
		{
			Clear();
			pendingInvitations = null;
			NextAllowedIngressUtcByKey = null;
			IngressInFlightByKey = null;
		}
	}
}