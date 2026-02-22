using System;
using System.Threading;
using FishMMO.Server.Core.Collections;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for party system state.
	/// Manages party invitations and database synchronization state separately from PartySystem logic.
	/// </summary>
	public class PartySystemRuntimeData : RuntimeDataContainer, IPartySystemRuntimeData
	{
		/// <summary>
		/// Tracks pending party invitations using a last-seen queue for O(1) touch and bounded TTL sweep.
		/// </summary>
		private LastSeenCacheTracker<long, long> pendingInvitations;

		/// <summary>
		/// Timestamp of the last successful database fetch for party updates.
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
		/// Initializes the party runtime data container.
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
		/// Clears all party runtime data.
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
		public bool TryGetPendingInvitation(long targetCharacterID, out long partyID)
		{
			if (pendingInvitations == null)
			{
				partyID = 0;
				return false;
			}

			return pendingInvitations.TryGetAndTouch(targetCharacterID, DateTime.UtcNow, out partyID);
		}

		/// <inheritdoc/>
		public bool TryAddPendingInvitation(long targetCharacterID, long partyID, DateTime nowUtc)
		{
			if (pendingInvitations == null)
			{
				return false;
			}

			if (pendingInvitations.TryGetAndTouch(targetCharacterID, nowUtc, out _))
			{
				return false;
			}

			pendingInvitations.Upsert(targetCharacterID, partyID, nowUtc);
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
		/// Deinitializes the party runtime data container.
		/// </summary>
		public override void Deinitialize()
		{
			Clear();
			pendingInvitations = null;
		}
	}
}