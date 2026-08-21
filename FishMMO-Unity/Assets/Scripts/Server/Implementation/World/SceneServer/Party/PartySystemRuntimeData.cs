using System;
using System.Collections.Generic;
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
		private LastSeenCacheTracker<long, PendingPartyInvitation> pendingInvitations;

		/// <summary>
		/// Tracks the last invitation each (inviter, target) pair produced, for the per-target
		/// invite cooldown.
		/// </summary>
		private LastSeenCacheTracker<(long inviter, long target), DateTime> inviteCooldowns;

		/// <summary>
		/// Characters whose party membership row is currently being deleted.
		/// </summary>
		/// <remarks>
		/// Main-thread only. Every writer is either a broadcast handler or a main-thread marshal.
		/// </remarks>
		private readonly HashSet<long> membershipRemovalsInFlight = new HashSet<long>();

		/// <summary>
		/// Timestamp of the last successful database fetch for party updates.
		/// </summary>
		public DateTime LastFetchTime { get; set; }

		/// <summary>
		/// Atomic state flag indicating whether an update pump is currently in flight (0 = idle, 1 = in-progress).
		/// </summary>
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
			pendingInvitations = new LastSeenCacheTracker<long, PendingPartyInvitation>();
			inviteCooldowns = new LastSeenCacheTracker<(long inviter, long target), DateTime>();
			membershipRemovalsInFlight.Clear();
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
			inviteCooldowns?.Clear();
			membershipRemovalsInFlight.Clear();
			LastFetchTime = DateTime.UtcNow;
			Interlocked.Exchange(ref updatePumpInFlight, 0);
			NextInvitationSweepUtc = DateTime.UtcNow;
			IngressGuard?.Clear();
		}

		/// <inheritdoc/>
		/// <remarks>
		/// The TTL the accept path enforces is measured against
		/// <see cref="PendingPartyInvitation.IssuedUtc"/>, which a reader cannot move. The touch
		/// performed here only moves the SWEEP's clock.
		/// </remarks>
		public bool TryGetPendingInvitation(long targetCharacterID, out PendingPartyInvitation invitation)
		{
			if (pendingInvitations == null)
			{
				invitation = default;
				return false;
			}

			return pendingInvitations.TryGetAndTouch(targetCharacterID, DateTime.UtcNow, out invitation);
		}

		/// <inheritdoc/>
		public bool TryAddPendingInvitation(long targetCharacterID, PendingPartyInvitation invitation)
		{
			if (pendingInvitations == null)
			{
				return false;
			}

			if (pendingInvitations.TryGetAndTouch(targetCharacterID, invitation.IssuedUtc, out _))
			{
				return false;
			}

			pendingInvitations.Upsert(targetCharacterID, invitation, invitation.IssuedUtc);
			return true;
		}

		/// <inheritdoc/>
		public bool TryBeginInviteCooldown(long inviterCharacterID, long targetCharacterID, TimeSpan cooldown, DateTime nowUtc)
		{
			if (inviteCooldowns == null)
			{
				return true;
			}

			(long inviter, long target) key = (inviterCharacterID, targetCharacterID);

			if (inviteCooldowns.TryGetAndTouch(key, nowUtc, out DateTime lastUtc) &&
				nowUtc - lastUtc < cooldown)
			{
				return false;
			}

			inviteCooldowns.Upsert(key, nowUtc, nowUtc);
			return true;
		}

		/// <inheritdoc/>
		public int SweepInviteCooldowns(DateTime nowUtc, TimeSpan ttl, int maxScan, int maxRemove)
		{
			if (inviteCooldowns == null)
			{
				return 0;
			}

			return inviteCooldowns.SweepExpired(nowUtc, ttl, maxScan, maxRemove);
		}

		/// <inheritdoc/>
		public void BeginMembershipRemoval(long characterID)
		{
			membershipRemovalsInFlight.Add(characterID);
		}

		/// <inheritdoc/>
		public void EndMembershipRemoval(long characterID)
		{
			membershipRemovalsInFlight.Remove(characterID);
		}

		/// <inheritdoc/>
		public bool IsMembershipRemovalInFlight(long characterID)
		{
			return membershipRemovalsInFlight.Contains(characterID);
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
		protected override void OnDeinitialize()
		{
			Clear();
			pendingInvitations = null;
			inviteCooldowns = null;
		}
	}
}