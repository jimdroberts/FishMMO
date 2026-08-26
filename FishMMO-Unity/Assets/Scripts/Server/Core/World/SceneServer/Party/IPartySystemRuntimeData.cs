using System;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// A party invitation the server is holding for one character.
	/// </summary>
	/// <remarks>
	/// The invitation used to be stored as a bare party ID, which meant an accept could only ever
	/// say "yes" to whatever happened to be pending — there was no way to tell the invitation the
	/// player was shown apart from the one that replaced it. Carrying the inviter and the issue
	/// time lets the accept path prove the two are the same invitation. See
	/// <c>PendingGuildInvitation</c> for the same reasoning on the guild side.
	/// </remarks>
	public readonly struct PendingPartyInvitation
	{
		/// <summary>The party the target was invited to.</summary>
		public readonly long PartyID;

		/// <summary>The character who sent the invitation.</summary>
		public readonly long InviterCharacterID;

		/// <summary>When the invitation was issued (UTC).</summary>
		public readonly DateTime IssuedUtc;

		/// <summary>
		/// Initializes a new pending party invitation.
		/// </summary>
		/// <param name="partyID">The party the target was invited to.</param>
		/// <param name="inviterCharacterID">The character who sent the invitation.</param>
		/// <param name="issuedUtc">When the invitation was issued (UTC).</param>
		public PendingPartyInvitation(long partyID, long inviterCharacterID, DateTime issuedUtc)
		{
			PartyID = partyID;
			InviterCharacterID = inviterCharacterID;
			IssuedUtc = issuedUtc;
		}
	}

	/// <summary>
	/// Runtime data container for party system state.
	/// Provides invitation operations with O(1) touch semantics and bounded TTL cleanup.
	/// </summary>
	public interface IPartySystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Tries to get a pending party invitation for the target character.
		/// </summary>
		/// <param name="targetCharacterID">Invited target character identifier.</param>
		/// <param name="invitation">Resolved invitation when found.</param>
		/// <returns>True when a pending invitation exists; otherwise false.</returns>
		bool TryGetPendingInvitation(long targetCharacterID, out PendingPartyInvitation invitation);

		/// <summary>
		/// Attempts to add a new pending invitation.
		/// </summary>
		/// <param name="targetCharacterID">Invited target character identifier.</param>
		/// <param name="invitation">The invitation to hold.</param>
		/// <returns>True when inserted; false when an invitation is already pending.</returns>
		bool TryAddPendingInvitation(long targetCharacterID, PendingPartyInvitation invitation);

		/// <summary>
		/// Records an invitation attempt against a specific target and reports whether it is
		/// allowed by the per-target cooldown.
		/// </summary>
		/// <param name="inviterCharacterID">The character sending the invitation.</param>
		/// <param name="targetCharacterID">The character being invited.</param>
		/// <param name="cooldown">Minimum interval between invitations to the same target.</param>
		/// <param name="nowUtc">Current UTC timestamp.</param>
		/// <returns>True when the invitation may proceed; false while the cooldown is active.</returns>
		/// <remarks>
		/// The pending-invitation slot is not a rate limit — declining clears it immediately — and
		/// the ingress debounce is per connection rather than per target, so neither stops one
		/// player from keeping a modal permanently on another player's screen.
		/// </remarks>
		bool TryBeginInviteCooldown(long inviterCharacterID, long targetCharacterID, TimeSpan cooldown, DateTime nowUtc);

		/// <summary>
		/// Sweeps expired invite cooldown entries using bounded scan/remove limits.
		/// </summary>
		/// <param name="nowUtc">Current UTC timestamp.</param>
		/// <param name="ttl">Cooldown entry time-to-live.</param>
		/// <param name="maxScan">Maximum queue entries to scan.</param>
		/// <param name="maxRemove">Maximum expired entries to remove.</param>
		/// <returns>Number of removed entries.</returns>
		int SweepInviteCooldowns(DateTime nowUtc, TimeSpan ttl, int maxScan, int maxRemove);

		/// <summary>
		/// Reports whether a party update has already been processed by this server.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The pump's watermark is deliberately held behind real time to absorb clock skew between
		/// scene servers, which means every update inside that window is fetched again on each
		/// tick — with a one-second pump and a five-second allowance, five times. Each re-fetch
		/// would otherwise re-read the roster, re-ask who is online, and re-broadcast the party to
		/// everybody in it, for an update whose work was finished on the first pass.
		/// </para>
		/// <para>
		/// Recording what has been handled makes the allowance free, so it can be sized for the
		/// worst skew worth tolerating rather than traded off against pump cost. The timestamp is
		/// the update row's own, and the row is only ever replaced by a strictly later one, so an
		/// update this server has seen is identified exactly rather than approximately.
		/// </para>
		/// </remarks>
		/// <param name="partyID">The party the update belongs to.</param>
		/// <param name="lastUpdateUtc">The update row's timestamp.</param>
		/// <returns>True when this update, or a later one, has already been processed.</returns>
		bool HasProcessedPartyUpdate(long partyID, DateTime lastUpdateUtc);

		/// <summary>
		/// Records a party update as processed.
		/// </summary>
		/// <param name="partyID">The party the update belongs to.</param>
		/// <param name="lastUpdateUtc">The update row's timestamp.</param>
		/// <remarks>
		/// Called only once the work has actually been handed off, never at the point the update is
		/// read. Marking on read would drop the update entirely whenever the hand-off failed — the
		/// pump would skip it ever after, and the roster change it carried would never reach
		/// anybody.
		/// </remarks>
		void MarkPartyUpdateProcessed(long partyID, DateTime lastUpdateUtc);

		/// <summary>
		/// Drops processed-update records for parties that have stopped changing.
		/// </summary>
		/// <param name="nowUtc">Current UTC timestamp.</param>
		/// <param name="ttl">Age past which a record is discarded.</param>
		/// <returns>The number of records removed.</returns>
		/// <remarks>
		/// A record only has to outlive the skew allowance — past that the watermark has moved
		/// beyond the update and it can never be re-fetched. Anything older belongs to a party
		/// that has gone quiet and would otherwise sit in the map for the lifetime of the process.
		/// </remarks>
		int SweepProcessedPartyUpdates(DateTime nowUtc, TimeSpan ttl);

		/// <summary>
		/// Records that a party's leader was observed to hold no session, and reports whether they
		/// have been observed that way for long enough to act on.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>A leader who is not logged in has not necessarily gone.</b> Moving between scene
		/// servers — walking through a teleporter, or entering the dungeon the party just opened —
		/// releases the character's session on the way out and re-claims it on arrival, and for
		/// the whole of that gap the database reports them exactly as it reports somebody who quit.
		/// Acting on a single observation would therefore take leadership away from a leader for
		/// the crime of leading their party into the instance, which is close to the worst possible
		/// moment for it.
		/// </para>
		/// <para>
		/// So absence has to be observed twice, far enough apart that a scene load cannot span it.
		/// The first observation only starts the clock; the second, once the grace has elapsed, is
		/// what confirms it. The clock is per (party, leader), so a leader who returns, or one who
		/// is replaced by some other route, resets it rather than inheriting somebody else's.
        /// </para>
		/// <para>
		/// Held in memory by whichever server is doing the observing rather than persisted. A
		/// server that dies mid-grace simply loses its half-finished observation, and the next
		/// server to look starts its own — which is the correct outcome, and one less piece of
		/// state that can be left behind.
		/// </para>
		/// </remarks>
		/// <param name="partyID">The party being examined.</param>
		/// <param name="leaderCharacterID">The member currently holding the rank.</param>
		/// <param name="nowUtc">Current UTC timestamp.</param>
		/// <param name="grace">How long the leader must be continuously absent.</param>
		/// <param name="dueUtc">
		/// When the grace elapses. Meaningful only when this returns false, so the caller can
		/// arrange to look again rather than leaving the observation to expire unread.
		/// </param>
		/// <returns>
		/// True when the absence has been confirmed and leadership may be moved. Confirming does
		/// not consume the observation: a caller that then fails to move the rank may try again at
		/// once rather than waiting out another grace period. Clear it with
		/// <see cref="ClearLeaderAbsence"/> once the rank has moved, or once the holder turns out
		/// to be present.
		/// </returns>
		bool TryConfirmLeaderAbsent(long partyID, long leaderCharacterID, DateTime nowUtc, TimeSpan grace, out DateTime dueUtc);

		/// <summary>
		/// Forgets any absence being tracked for a party.
		/// </summary>
		/// <param name="partyID">The party to clear.</param>
		void ClearLeaderAbsence(long partyID);

		/// <summary>
		/// Drops absence observations that nothing has come back to finish.
		/// </summary>
		/// <param name="nowUtc">Current UTC timestamp.</param>
		/// <param name="ttl">Age past which an unfinished observation is discarded.</param>
		/// <returns>The number of observations removed.</returns>
		/// <remarks>
		/// An observation is resolved within one grace period by whatever scheduled the second
		/// look. One that outlives several belongs to a party nothing is examining any more — its
		/// last member left this server — and would otherwise sit in the map for the lifetime of
		/// the process.
		/// </remarks>
		int SweepLeaderAbsences(DateTime nowUtc, TimeSpan ttl);

		/// <summary>
		/// Claims exclusive rights to mutate one party's membership or ranks.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>This is what makes leadership free of races.</b> Every write that can change who
		/// leads a party — leaving, being kicked, a promotion, a leadership hand-off after a
		/// disconnect, and the leaderless repair on an instance join — reads the party's rows,
		/// decides from them, and writes back, across several <c>await</c>s. Two of those running
		/// against the same party interleave their reads and their writes, and both decisions are
		/// then made from a roster that no longer exists.
		/// </para>
		/// <para>
		/// The concrete failure is a party with two leaders. A leader promotes a member and then
		/// leaves in the same breath: the promotion reads the roster and starts handing the rank
		/// to its target, while the leave reads the same roster, sees a party about to be left
		/// without a leader, and hands the rank to somebody else. Both writes are version-gated
		/// and both succeed, because they touch different rows. Optimistic concurrency cannot see
		/// this — the two writes do not conflict; the two DECISIONS do.
		/// </para>
		/// <para>
		/// Per party rather than per connection, because that is the granularity of the shared
		/// state. The existing ingress guard is per (connection, operation), so it does not stop
		/// one player's leave from racing their own promote, let alone two players racing each
		/// other. Refused rather than queued: the caller answers the client with a busy response
		/// it can retry, which is honest about a mutation not having happened in a way that
		/// silently dropping it or serialising it behind an unbounded queue would not be.
		/// </para>
		/// <para>
		/// <b>Process-local, and deliberately so.</b> Two scene servers can each hold "the claim"
		/// for the same party at the same time; this removes the races within one server, not
		/// across the shard. Making it distributed would mean a lock in the database on a path
		/// taken every time anybody leaves a party, and it would not buy correctness — what makes
		/// the cross-server case safe is that every write is version-gated and every leadership
		/// decision is re-derived from the rows by a repair that converges. Two servers that
		/// disagree produce a state that is wrong for one pass and right afterwards; the claim is
		/// what stops the far more frequent same-server case from getting there at all.
		/// </para>
		/// </remarks>
		/// <param name="partyID">The party to claim.</param>
		/// <param name="token">
		/// Receives the claim's identity, to be handed back to <see cref="EndPartyMutation"/>.
		/// A claim is abandoned after a generous timeout so a task that dies without releasing
		/// cannot lock a party out of every future change; the token is what stops the late
		/// release from that task then freeing a claim somebody else has since taken.
		/// </param>
		/// <returns>True when the claim was granted.</returns>
		bool TryBeginPartyMutation(long partyID, out long token);

		/// <summary>
		/// Releases a claim taken by <see cref="TryBeginPartyMutation"/>.
		/// </summary>
		/// <param name="partyID">The party claimed.</param>
		/// <param name="token">The token the claim was granted with.</param>
		void EndPartyMutation(long partyID, long token);

		/// <summary>
		/// Marks a character's party membership as being removed.
		/// </summary>
		/// <param name="characterID">The character leaving or being kicked.</param>
		/// <remarks>
		/// Kick and leave both delete the membership row from a background task while the
		/// character still holds a live <c>IPartyController.ID</c>. Disconnecting inside that
		/// window ran the ordinary disconnect persist, which upserts the row back from that live
		/// controller and puts the player straight back into the party they just left.
		/// </remarks>
		void BeginMembershipRemoval(long characterID);

		/// <summary>
		/// Clears the membership-removal marker for a character.
		/// </summary>
		/// <param name="characterID">The character whose removal has finished.</param>
		void EndMembershipRemoval(long characterID);

		/// <summary>
		/// Reports whether a character's party membership is currently being removed.
		/// </summary>
		/// <param name="characterID">The character to test.</param>
		/// <returns>True while a removal is in flight.</returns>
		bool IsMembershipRemovalInFlight(long characterID);

		/// <summary>
		/// Removes a pending invitation for a target character.
		/// </summary>
		/// <param name="targetCharacterID">Invited target character identifier.</param>
		/// <returns>True when removed; otherwise false.</returns>
		bool RemovePendingInvitation(long targetCharacterID);

		/// <summary>
		/// Sweeps expired invitations using bounded scan/remove limits.
		/// </summary>
		/// <param name="nowUtc">Current UTC timestamp.</param>
		/// <param name="ttl">Invitation time-to-live.</param>
		/// <param name="maxScan">Maximum queue entries to scan.</param>
		/// <param name="maxRemove">Maximum expired entries to remove.</param>
		/// <returns>Number of removed invitations.</returns>
		int SweepExpiredInvitations(DateTime nowUtc, TimeSpan ttl, int maxScan, int maxRemove);

		/// <summary>
		/// Timestamp of the last successful database fetch for party updates.
		/// </summary>
		DateTime LastFetchTime { get; set; }

		/// <summary>
		/// Atomically transitions the update pump from idle to in-flight.
		/// Returns true if this call won the race; false if a pump is already in flight.
		/// </summary>
		bool TryBeginUpdatePump();

		/// <summary>
		/// Atomically transitions the update pump from in-flight back to idle.
		/// </summary>
		void EndUpdatePump();

		/// <summary>
		/// Next scheduled UTC time for invitation cleanup.
		/// </summary>
		DateTime NextInvitationSweepUtc { get; set; }

		/// <summary>
		/// Shared ingress guard for per-connection per-operation debounce and in-flight tracking.
		/// </summary>
		IngressGuard IngressGuard { get; }
	}
}