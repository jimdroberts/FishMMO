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
		/// <remarks>
		/// This tracker and both cooldown trackers run on <see cref="MonotonicClock"/>, through
		/// their monotonic overloads: a TTL and a cooldown are local durations. Only the
		/// processed-update record is on the wall clock, because it holds the update rows'
		/// database timestamps.
		/// </remarks>
		private LastSeenCacheTracker<long, PendingGuildInvitation> pendingInvitations;

		/// <summary>
		/// Tracks the last invitation each (inviter, target) pair produced, for the per-target
		/// invite cooldown, as <see cref="MonotonicClock"/> seconds.
		/// </summary>
		private LastSeenCacheTracker<(long inviter, long target), double> inviteCooldowns;

		/// <summary>
		/// Tracks the last guild application each character submitted, for the application rate
		/// limit, as <see cref="MonotonicClock"/> seconds.
		/// </summary>
		private LastSeenCacheTracker<long, double> applicationCooldowns;

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
		/// The newest guild update this server has finished processing, per guild.
		/// </summary>
		/// <remarks>
		/// Read on the pump's worker and written after its main-thread work is queued, and swept
		/// from the main thread, so it is guarded by <see cref="processedGuildUpdatesGate"/>.
		/// </remarks>
		private readonly Dictionary<long, DateTime> processedGuildUpdates = new Dictionary<long, DateTime>();

		/// <summary>Scratch key list for the processed-update sweep.</summary>
		private readonly List<long> processedGuildUpdateSweepBuffer = new List<long>();

		/// <summary>Guards <see cref="processedGuildUpdates"/> and its sweep buffer.</summary>
		private readonly object processedGuildUpdatesGate = new object();

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
		public double NextInvitationSweepAt { get; set; }

		/// <inheritdoc/>
		public IngressGuard IngressGuard { get; private set; }

		/// <summary>
		/// Initializes the guild runtime data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			pendingInvitations = new LastSeenCacheTracker<long, PendingGuildInvitation>();
			inviteCooldowns = new LastSeenCacheTracker<(long inviter, long target), double>();
			applicationCooldowns = new LastSeenCacheTracker<long, double>();
			membershipRemovalsInFlight.Clear();
			ClearProcessedGuildUpdates();
			LastFetchTime = DateTime.UtcNow;
			Interlocked.Exchange(ref updatePumpInFlight, 0);
			NextInvitationSweepAt = MonotonicClock.NowSeconds;
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
			ClearProcessedGuildUpdates();
			LastFetchTime = DateTime.UtcNow;
			Interlocked.Exchange(ref updatePumpInFlight, 0);
			NextInvitationSweepAt = MonotonicClock.NowSeconds;
			IngressGuard?.Clear();
		}

		/// <inheritdoc/>
		/// <remarks>
		/// Deliberately does NOT touch the entry's last-seen timestamp. The TTL is measured
		/// against <see cref="PendingGuildInvitation.IssuedAt"/>, which the reader cannot move;
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
			 * it here is harmless — the authoritative expiry check reads IssuedAt. */
			return pendingInvitations.TryGetAndTouch(targetCharacterID, MonotonicClock.NowSeconds, out invitation);
		}

		/// <inheritdoc/>
		public bool TryAddPendingInvitation(long targetCharacterID, PendingGuildInvitation invitation)
		{
			if (pendingInvitations == null)
			{
				return false;
			}

			if (pendingInvitations.TryGetAndTouch(targetCharacterID, invitation.IssuedAt, out _))
			{
				return false;
			}

			pendingInvitations.Upsert(targetCharacterID, invitation, invitation.IssuedAt);
			return true;
		}

		/// <inheritdoc/>
		public bool RemovePendingInvitation(long targetCharacterID)
		{
			pendingInvitations?.Remove(targetCharacterID);
			return true;
		}

		/// <inheritdoc/>
		public int SweepExpiredInvitations(double now, TimeSpan ttl, int maxScan, int maxRemove)
		{
			if (pendingInvitations == null)
			{
				return 0;
			}

			return pendingInvitations.SweepExpired(now, ttl, maxScan, maxRemove);
		}

		/// <inheritdoc/>
		public bool TryBeginInviteCooldown(long inviterCharacterID, long targetCharacterID, TimeSpan cooldown, double now)
		{
			if (inviteCooldowns == null)
			{
				return true;
			}

			(long inviter, long target) key = (inviterCharacterID, targetCharacterID);

			if (inviteCooldowns.TryGetAndTouch(key, now, out double last) &&
				now - last < cooldown.TotalSeconds)
			{
				return false;
			}

			inviteCooldowns.Upsert(key, now, now);
			return true;
		}

		/// <inheritdoc/>
		public int SweepInviteCooldowns(double now, TimeSpan ttl, int maxScan, int maxRemove)
		{
			if (inviteCooldowns == null)
			{
				return 0;
			}

			return inviteCooldowns.SweepExpired(now, ttl, maxScan, maxRemove);
		}

		/// <inheritdoc/>
		public bool TryBeginApplicationCooldown(long characterID, TimeSpan cooldown, double now)
		{
			if (applicationCooldowns == null)
			{
				return true;
			}

			if (applicationCooldowns.TryGetAndTouch(characterID, now, out double last) &&
				now - last < cooldown.TotalSeconds)
			{
				return false;
			}

			applicationCooldowns.Upsert(characterID, now, now);
			return true;
		}

		/// <inheritdoc/>
		public int SweepApplicationCooldowns(double now, TimeSpan ttl, int maxScan, int maxRemove)
		{
			if (applicationCooldowns == null)
			{
				return 0;
			}

			return applicationCooldowns.SweepExpired(now, ttl, maxScan, maxRemove);
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
		public bool HasProcessedGuildUpdate(long guildID, DateTime lastUpdateUtc)
		{
			if (guildID <= 0)
			{
				return false;
			}

			lock (processedGuildUpdatesGate)
			{
				return processedGuildUpdates.TryGetValue(guildID, out DateTime processedUtc) &&
					   processedUtc >= lastUpdateUtc;
			}
		}

		/// <inheritdoc/>
		public void MarkGuildUpdateProcessed(long guildID, DateTime lastUpdateUtc)
		{
			if (guildID <= 0)
			{
				return;
			}

			lock (processedGuildUpdatesGate)
			{
				/* Never moved backwards: a fetch can return an older and a newer update for one
				 * guild in either order, and remembering the older would let the newer be
				 * delivered a second time. */
				if (processedGuildUpdates.TryGetValue(guildID, out DateTime processedUtc) &&
					processedUtc >= lastUpdateUtc)
				{
					return;
				}

				processedGuildUpdates[guildID] = lastUpdateUtc;
			}
		}

		/// <inheritdoc/>
		public int SweepProcessedGuildUpdates(DateTime nowUtc, TimeSpan ttl)
		{
			if (ttl <= TimeSpan.Zero)
			{
				return 0;
			}

			lock (processedGuildUpdatesGate)
			{
				if (processedGuildUpdates.Count < 1)
				{
					return 0;
				}

				processedGuildUpdateSweepBuffer.Clear();
				foreach (KeyValuePair<long, DateTime> entry in processedGuildUpdates)
				{
					if (nowUtc - entry.Value > ttl)
					{
						processedGuildUpdateSweepBuffer.Add(entry.Key);
					}
				}

				for (int i = 0; i < processedGuildUpdateSweepBuffer.Count; ++i)
				{
					processedGuildUpdates.Remove(processedGuildUpdateSweepBuffer[i]);
				}

				int removed = processedGuildUpdateSweepBuffer.Count;
				processedGuildUpdateSweepBuffer.Clear();
				return removed;
			}
		}

		/// <summary>
		/// Forgets every processed-update record.
		/// </summary>
		private void ClearProcessedGuildUpdates()
		{
			lock (processedGuildUpdatesGate)
			{
				processedGuildUpdates.Clear();
				processedGuildUpdateSweepBuffer.Clear();
			}
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
