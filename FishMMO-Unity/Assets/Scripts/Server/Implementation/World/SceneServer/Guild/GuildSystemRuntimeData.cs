using System;
using System.Collections.Generic;
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
		private LastSeenCacheTracker<long, PendingGuildInvitation> pendingInvitations;

		/// <summary>
		/// Tracks the last invitation each (inviter, target) pair produced, for the per-target
		/// invite cooldown.
		/// </summary>
		private LastSeenCacheTracker<(long inviter, long target), DateTime> inviteCooldowns;

		/// <summary>
		/// Tracks the last guild application each character submitted, for the application rate
		/// limit.
		/// </summary>
		private LastSeenCacheTracker<long, DateTime> applicationCooldowns;

		/// <summary>
		/// Characters whose guild membership row is currently being deleted.
		/// </summary>
		/// <remarks>
		/// Main-thread only. Every writer is either a broadcast handler or a main-thread marshal,
		/// so no lock is needed — and taking one would be misleading about where this is used.
		/// </remarks>
		private readonly HashSet<long> membershipRemovalsInFlight = new HashSet<long>();

		/// <summary>
		/// Timestamp of the last successful database fetch for guild updates.
		/// </summary>
		public DateTime LastFetchTime { get; set; }

		/// <summary>
		/// Tracks whether a guild update pump operation is currently in flight.
		/// Used with Interlocked to ensure only one pump runs at a time.
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
		/// Initializes the guild runtime data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			pendingInvitations = new LastSeenCacheTracker<long, PendingGuildInvitation>();
			inviteCooldowns = new LastSeenCacheTracker<(long inviter, long target), DateTime>();
			applicationCooldowns = new LastSeenCacheTracker<long, DateTime>();
			membershipRemovalsInFlight.Clear();
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
			inviteCooldowns?.Clear();
			applicationCooldowns?.Clear();
			membershipRemovalsInFlight.Clear();
			LastFetchTime = DateTime.UtcNow;
			Interlocked.Exchange(ref updatePumpInFlight, 0);
			NextInvitationSweepUtc = DateTime.UtcNow;
			IngressGuard?.Clear();
		}

		/// <inheritdoc/>
		/// <remarks>
		/// Deliberately does NOT touch the entry's last-seen timestamp. The TTL is measured
		/// against <see cref="PendingGuildInvitation.IssuedUtc"/>, which the reader cannot move;
		/// touching on read would let a late accept refresh the very entry the sweep was about to
		/// expire, which is exactly the behaviour that let a stale dialog stay live indefinitely.
		/// </remarks>
		public bool TryGetPendingInvitation(long targetCharacterID, out PendingGuildInvitation invitation)
		{
			if (pendingInvitations == null)
			{
				invitation = default;
				return false;
			}

			/* TryGetAndTouch is the only read the tracker offers. The touch it performs moves the
			 * SWEEP's clock, not the issue time the accept path validates against, so re-stamping
			 * it here is harmless — the authoritative expiry check reads IssuedUtc. */
			return pendingInvitations.TryGetAndTouch(targetCharacterID, DateTime.UtcNow, out invitation);
		}

		/// <inheritdoc/>
		public bool TryAddPendingInvitation(long targetCharacterID, PendingGuildInvitation invitation)
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
		public bool TryBeginApplicationCooldown(long characterID, TimeSpan cooldown, DateTime nowUtc)
		{
			if (applicationCooldowns == null)
			{
				return true;
			}

			if (applicationCooldowns.TryGetAndTouch(characterID, nowUtc, out DateTime lastUtc) &&
				nowUtc - lastUtc < cooldown)
			{
				return false;
			}

			applicationCooldowns.Upsert(characterID, nowUtc, nowUtc);
			return true;
		}

		/// <inheritdoc/>
		public int SweepApplicationCooldowns(DateTime nowUtc, TimeSpan ttl, int maxScan, int maxRemove)
		{
			if (applicationCooldowns == null)
			{
				return 0;
			}

			return applicationCooldowns.SweepExpired(nowUtc, ttl, maxScan, maxRemove);
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

		/// <summary>
		/// Deinitializes the guild runtime data container.
		/// </summary>
		protected override void OnDeinitialize()
		{
			Clear();
			pendingInvitations = null;
			inviteCooldowns = null;
			applicationCooldowns = null;
		}
	}
}
