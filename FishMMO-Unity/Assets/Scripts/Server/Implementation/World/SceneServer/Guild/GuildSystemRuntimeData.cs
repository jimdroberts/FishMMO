using System;
using System.Threading;
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

		private int updatePumpInFlight;

		/// <inheritdoc/>
		public bool TryBeginUpdatePump()
		{
			return Interlocked.CompareExchange(ref updatePumpInFlight, 1, 0) == 0;
		}

		/// <inheritdoc/>
		public void EndUpdatePump()
		{
			Interlocked.Exchange(ref updatePumpInFlight, 0);
		}

		/// <inheritdoc/>
		public DateTime NextInvitationSweepUtc { get; set; }

		/// <inheritdoc/>
		public IngressGuard IngressGuard { get; private set; }

		/// <summary>
		/// Initializes the guild runtime data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			pendingInvitations = new LastSeenCacheTracker<long, long>();
			LastFetchTime = DateTime.UtcNow;
			Interlocked.Exchange(ref updatePumpInFlight, 0);
			NextInvitationSweepUtc = DateTime.UtcNow;
			IngressGuard = new IngressGuard();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all guild runtime data.
		/// </summary>
		public override void Clear()
		{
			pendingInvitations?.Clear();
			LastFetchTime = DateTime.UtcNow;
			Interlocked.Exchange(ref updatePumpInFlight, 0);
			NextInvitationSweepUtc = DateTime.UtcNow;
			IngressGuard?.Clear();
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
		protected override void OnDeinitialize()
		{
			Clear();
			pendingInvitations = null;
		}
	}
}