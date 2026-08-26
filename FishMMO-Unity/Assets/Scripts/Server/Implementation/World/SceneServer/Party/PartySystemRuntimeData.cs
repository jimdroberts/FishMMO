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
		/// Live party-mutation claims, keyed by party ID. See <see cref="TryBeginPartyMutation"/>.
		/// </summary>
		private readonly Dictionary<long, PartyMutationClaim> partyMutations = new Dictionary<long, PartyMutationClaim>();

		/// <summary>
		/// Absence observations in progress, keyed by party. See <see cref="TryConfirmLeaderAbsent"/>.
		/// </summary>
		private readonly Dictionary<long, LeaderAbsence> leaderAbsences = new Dictionary<long, LeaderAbsence>();

		/// <summary>Scratch key list for the absence sweep.</summary>
		private readonly List<long> leaderAbsenceSweepBuffer = new List<long>();

		/// <summary>
		/// The newest party update this server has finished processing, per party.
		/// </summary>
		private readonly Dictionary<long, DateTime> processedPartyUpdates = new Dictionary<long, DateTime>();

		/// <summary>Scratch key list for the processed-update sweep.</summary>
		private readonly List<long> processedPartyUpdateSweepBuffer = new List<long>();

		/// <summary>
		/// One party's in-progress observation that its leader holds no session.
		/// </summary>
		private readonly struct LeaderAbsence
		{
			/// <summary>The member observed absent. A different holder restarts the clock.</summary>
			public readonly long LeaderCharacterID;

			/// <summary>When the absence was first observed (UTC).</summary>
			public readonly DateTime FirstSeenUtc;

			/// <summary>
			/// Initializes an observation.
			/// </summary>
			/// <param name="leaderCharacterID">The member observed absent.</param>
			/// <param name="firstSeenUtc">When it was first observed.</param>
			public LeaderAbsence(long leaderCharacterID, DateTime firstSeenUtc)
			{
				LeaderCharacterID = leaderCharacterID;
				FirstSeenUtc = firstSeenUtc;
			}
		}

		/// <summary>
		/// Guards <see cref="partyMutations"/> and <see cref="leaderAbsences"/>.
		/// </summary>
		/// <remarks>
		/// Locked rather than main-thread-only, unlike the removal set above. Claims are taken on
		/// the main thread by broadcast handlers but released from the background tasks that hold
		/// them, and marshalling every release back would put the party's next change a frame
		/// behind the write that finished it.
		/// </remarks>
		private readonly object partyMutationGate = new object();

		/// <summary>
		/// Source of claim tokens. Monotonic, so a token is never reused.
		/// </summary>
		private long nextPartyMutationToken;

		/// <summary>
		/// How long a claim is honoured before it is treated as abandoned.
		/// </summary>
		/// <remarks>
		/// Far longer than any party mutation takes — several database round trips — and short
		/// enough that a task which died without releasing does not lock a party out of every
		/// future change for the lifetime of the server. It is a backstop, not a timeout: nothing
		/// is expected to reach it.
		/// </remarks>
		private static readonly TimeSpan PartyMutationTtl = TimeSpan.FromSeconds(30.0);

		/// <summary>
		/// One outstanding claim on a party.
		/// </summary>
		private readonly struct PartyMutationClaim
		{
			/// <summary>Identity handed to the claimant, and required to release.</summary>
			public readonly long Token;
			/// <summary>When the claim was granted (UTC).</summary>
			public readonly DateTime GrantedUtc;

			/// <summary>
			/// Initializes a claim.
			/// </summary>
			/// <param name="token">The claim's identity.</param>
			/// <param name="grantedUtc">When it was granted.</param>
			public PartyMutationClaim(long token, DateTime grantedUtc)
			{
				Token = token;
				GrantedUtc = grantedUtc;
			}
		}

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
			lock (partyMutationGate)
			{
				partyMutations.Clear();
				leaderAbsences.Clear();
				leaderAbsenceSweepBuffer.Clear();
				processedPartyUpdates.Clear();
				processedPartyUpdateSweepBuffer.Clear();
			}
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
			lock (partyMutationGate)
			{
				partyMutations.Clear();
				leaderAbsences.Clear();
				leaderAbsenceSweepBuffer.Clear();
				processedPartyUpdates.Clear();
				processedPartyUpdateSweepBuffer.Clear();
			}
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

		/// <inheritdoc/>
		public bool TryBeginPartyMutation(long partyID, out long token)
		{
			token = 0;
			if (partyID <= 0)
			{
				return false;
			}

			DateTime nowUtc = DateTime.UtcNow;

			lock (partyMutationGate)
			{
				if (partyMutations.TryGetValue(partyID, out PartyMutationClaim existing) &&
					nowUtc - existing.GrantedUtc < PartyMutationTtl)
				{
					return false;
				}

				token = ++nextPartyMutationToken;
				partyMutations[partyID] = new PartyMutationClaim(token, nowUtc);
				return true;
			}
		}

		/// <inheritdoc/>
		public void EndPartyMutation(long partyID, long token)
		{
			if (partyID <= 0 || token == 0)
			{
				return;
			}

			lock (partyMutationGate)
			{
				/* Only the holder may release. A task that overran the TTL and finished after its
				 * claim was reissued would otherwise free the claim its successor is working
				 * under, putting two mutations back on the same party — which is the exact state
				 * the claim exists to prevent, reintroduced by the safety valve. */
				if (partyMutations.TryGetValue(partyID, out PartyMutationClaim existing) &&
					existing.Token == token)
				{
					partyMutations.Remove(partyID);
				}
			}
		}


		/// <inheritdoc/>
		public bool TryConfirmLeaderAbsent(long partyID, long leaderCharacterID, DateTime nowUtc, TimeSpan grace, out DateTime dueUtc)
		{
			dueUtc = nowUtc;

			if (partyID <= 0 || leaderCharacterID <= 0)
			{
				return false;
			}

			lock (partyMutationGate)
			{
				if (!leaderAbsences.TryGetValue(partyID, out LeaderAbsence absence) ||
					absence.LeaderCharacterID != leaderCharacterID)
				{
					/* First sighting, or the rank has moved since the last one. Either way the
					 * clock starts now — an observation of one member's absence says nothing about
					 * another's. */
					leaderAbsences[partyID] = new LeaderAbsence(leaderCharacterID, nowUtc);
					dueUtc = nowUtc + grace;
					return false;
				}

				DateTime confirmedUtc = absence.FirstSeenUtc + grace;
				if (nowUtc < confirmedUtc)
				{
					dueUtc = confirmedUtc;
					return false;
				}

				/* Confirmed, and deliberately NOT consumed. The caller has several ways to fail
				 * after this point — the successor's row moved, the promotion was refused — and
				 * clearing the observation here would make each of those cost another full grace
				 * period before anything could try again, for a party that is already stuck. The
				 * observation is cleared when leadership actually moves, or when the holder turns
				 * out to be present after all, and swept if neither ever happens. */
				return true;
			}
		}

		/// <inheritdoc/>
		public void ClearLeaderAbsence(long partyID)
		{
			if (partyID <= 0)
			{
				return;
			}

			lock (partyMutationGate)
			{
				leaderAbsences.Remove(partyID);
			}
		}

		/// <inheritdoc/>
		public int SweepLeaderAbsences(DateTime nowUtc, TimeSpan ttl)
		{
			if (ttl <= TimeSpan.Zero)
			{
				return 0;
			}

			lock (partyMutationGate)
			{
				if (leaderAbsences.Count < 1)
				{
					return 0;
				}

				leaderAbsenceSweepBuffer.Clear();
				foreach (KeyValuePair<long, LeaderAbsence> entry in leaderAbsences)
				{
					if (nowUtc - entry.Value.FirstSeenUtc > ttl)
					{
						leaderAbsenceSweepBuffer.Add(entry.Key);
					}
				}

				for (int i = 0; i < leaderAbsenceSweepBuffer.Count; ++i)
				{
					leaderAbsences.Remove(leaderAbsenceSweepBuffer[i]);
				}

				int removed = leaderAbsenceSweepBuffer.Count;
				leaderAbsenceSweepBuffer.Clear();
				return removed;
			}
		}

		/// <inheritdoc/>
		public bool HasProcessedPartyUpdate(long partyID, DateTime lastUpdateUtc)
		{
			if (partyID <= 0)
			{
				return false;
			}

			lock (partyMutationGate)
			{
				return processedPartyUpdates.TryGetValue(partyID, out DateTime processedUtc) &&
					   processedUtc >= lastUpdateUtc;
			}
		}

		/// <inheritdoc/>
		public void MarkPartyUpdateProcessed(long partyID, DateTime lastUpdateUtc)
		{
			if (partyID <= 0)
			{
				return;
			}

			lock (partyMutationGate)
			{
				/* Never moved backwards. Two updates for one party can be handled out of order
				 * when a fetch returns both, and remembering the older one would let the newer be
				 * processed a second time. */
				if (processedPartyUpdates.TryGetValue(partyID, out DateTime processedUtc) &&
					processedUtc >= lastUpdateUtc)
				{
					return;
				}

				processedPartyUpdates[partyID] = lastUpdateUtc;
			}
		}

		/// <inheritdoc/>
		public int SweepProcessedPartyUpdates(DateTime nowUtc, TimeSpan ttl)
		{
			if (ttl <= TimeSpan.Zero)
			{
				return 0;
			}

			lock (partyMutationGate)
			{
				if (processedPartyUpdates.Count < 1)
				{
					return 0;
				}

				processedPartyUpdateSweepBuffer.Clear();
				foreach (KeyValuePair<long, DateTime> entry in processedPartyUpdates)
				{
					if (nowUtc - entry.Value > ttl)
					{
						processedPartyUpdateSweepBuffer.Add(entry.Key);
					}
				}

				for (int i = 0; i < processedPartyUpdateSweepBuffer.Count; ++i)
				{
					processedPartyUpdates.Remove(processedPartyUpdateSweepBuffer[i]);
				}

				int removed = processedPartyUpdateSweepBuffer.Count;
				processedPartyUpdateSweepBuffer.Clear();
				return removed;
			}
		}
	}
}