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