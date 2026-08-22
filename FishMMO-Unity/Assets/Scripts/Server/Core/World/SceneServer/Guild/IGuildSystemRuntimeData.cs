using System;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// A guild invitation the server is holding for one character.
	/// </summary>
	/// <remarks>
	/// The invitation used to be stored as a bare guild ID, which meant an accept could only ever
	/// say "yes" to whatever happened to be pending — there was no way to tell the invitation the
	/// player was shown apart from the one that replaced it. Carrying the inviter and the issue
	/// time lets the accept path prove the two are the same invitation.
	///
	/// <see cref="IssuedUtc"/> is stamped here rather than left to the cache's last-seen clock
	/// because reading the entry TOUCHES that clock: a client that polls, or simply an accept
	/// arriving late, refreshed the very timestamp the expiry sweep was about to act on. The
	/// stamped time is the one the TTL is measured against.
	/// </remarks>
	public readonly struct PendingGuildInvitation
	{
		/// <summary>The guild the target was invited to.</summary>
		public readonly long GuildID;

		/// <summary>The character who sent the invitation.</summary>
		public readonly long InviterCharacterID;

		/// <summary>When the invitation was issued (UTC).</summary>
		public readonly DateTime IssuedUtc;

		/// <summary>
		/// Initializes a new pending guild invitation.
		/// </summary>
		/// <param name="guildID">The guild the target was invited to.</param>
		/// <param name="inviterCharacterID">The character who sent the invitation.</param>
		/// <param name="issuedUtc">When the invitation was issued (UTC).</param>
		public PendingGuildInvitation(long guildID, long inviterCharacterID, DateTime issuedUtc)
		{
			GuildID = guildID;
			InviterCharacterID = inviterCharacterID;
			IssuedUtc = issuedUtc;
		}
	}

	/// <summary>
	/// Runtime data container for guild system state.
	/// Provides invitation operations with O(1) touch semantics and bounded TTL cleanup.
	/// </summary>
	public interface IGuildSystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Tries to get a pending guild invitation for the target character.
		/// </summary>
		/// <param name="targetCharacterID">Invited target character identifier.</param>
		/// <param name="invitation">Resolved invitation when found.</param>
		/// <returns>True when a pending invitation exists; otherwise false.</returns>
		bool TryGetPendingInvitation(long targetCharacterID, out PendingGuildInvitation invitation);

		/// <summary>
		/// Attempts to add a new pending invitation.
		/// </summary>
		/// <param name="targetCharacterID">Invited target character identifier.</param>
		/// <param name="invitation">The invitation to hold.</param>
		/// <returns>True when inserted; false when an invitation is already pending.</returns>
		bool TryAddPendingInvitation(long targetCharacterID, PendingGuildInvitation invitation);

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
		/// Records an invitation attempt against a specific target and reports whether it is
		/// allowed by the per-target cooldown.
		/// </summary>
		/// <param name="inviterCharacterID">The character sending the invitation.</param>
		/// <param name="targetCharacterID">The character being invited.</param>
		/// <param name="cooldown">Minimum interval between invitations to the same target.</param>
		/// <param name="nowUtc">Current UTC timestamp.</param>
		/// <returns>True when the invitation may proceed; false while the cooldown is active.</returns>
		/// <remarks>
		/// The pending-invitation slot is NOT a rate limit: declining clears it immediately, so an
		/// inviter could re-send the moment the target dismissed the dialog and keep a modal on
		/// their screen indefinitely. The connection-level debounce does not help either — it is a
		/// hundred milliseconds and is per connection, not per target, so it caps the rate without
		/// capping the harassment. This is the per-(inviter, target) limit that does.
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
		/// Begins the per-character guild application cooldown, if it is not already running.
		/// </summary>
		/// <param name="characterID">The applying character.</param>
		/// <param name="cooldown">Minimum interval between applications.</param>
		/// <param name="nowUtc">Current UTC time.</param>
		/// <returns>True when the application may proceed.</returns>
		/// <remarks>
		/// Keyed on the APPLICANT alone, not on (applicant, guild). The per-guild case is already
		/// covered by the unique index on the application table; what is not covered, and what
		/// this exists for, is one player applying to every guild in the directory in turn.
		/// </remarks>
		bool TryBeginApplicationCooldown(long characterID, TimeSpan cooldown, DateTime nowUtc);

		/// <summary>
		/// Removes expired application cooldown entries.
		/// </summary>
		/// <param name="nowUtc">Current UTC time.</param>
		/// <param name="ttl">Entry lifetime.</param>
		/// <param name="maxScan">Maximum entries scanned.</param>
		/// <param name="maxRemove">Maximum entries removed.</param>
		/// <returns>The number of entries removed.</returns>
		int SweepApplicationCooldowns(DateTime nowUtc, TimeSpan ttl, int maxScan, int maxRemove);

		/// <summary>
		/// Marks a character's guild membership as being removed.
		/// </summary>
		/// <param name="characterID">The character leaving or being kicked.</param>
		/// <remarks>
		/// Kick and leave both delete the membership row from a background task, and the character
		/// keeps a live <c>IGuildController.ID</c> until the delete lands. Disconnecting inside
		/// that window ran the ordinary disconnect persist, which upserts the membership row from
		/// that still-live controller — putting the player straight back into the guild they had
		/// just been removed from. This flag is what the disconnect path checks so it does not
		/// resurrect a membership that is being deleted.
		/// </remarks>
		void BeginMembershipRemoval(long characterID);

		/// <summary>
		/// Clears the membership-removal marker for a character.
		/// </summary>
		/// <param name="characterID">The character whose removal has finished.</param>
		void EndMembershipRemoval(long characterID);

		/// <summary>
		/// Reports whether a character's guild membership is currently being removed.
		/// </summary>
		/// <param name="characterID">The character to test.</param>
		/// <returns>True while a removal is in flight.</returns>
		bool IsMembershipRemovalInFlight(long characterID);

		/// <summary>
		/// Timestamp of the last successful database fetch for guild updates.
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
