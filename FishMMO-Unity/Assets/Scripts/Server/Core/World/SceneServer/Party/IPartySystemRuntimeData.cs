using System;
using System.Collections.Concurrent;

namespace FishMMO.Server.Core.World.SceneServer
{
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
		/// <param name="partyID">Resolved party identifier when found.</param>
		/// <returns>True when a pending invitation exists; otherwise false.</returns>
		bool TryGetPendingInvitation(long targetCharacterID, out long partyID);

		/// <summary>
		/// Attempts to add a new pending invitation.
		/// </summary>
		/// <param name="targetCharacterID">Invited target character identifier.</param>
		/// <param name="partyID">Inviting party identifier.</param>
		/// <param name="nowUtc">Current UTC timestamp.</param>
		/// <returns>True when inserted; false when the invitation already exists.</returns>
		bool TryAddPendingInvitation(long targetCharacterID, long partyID, DateTime nowUtc);

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
		/// Atomic in-flight gate for periodic update work.
		/// </summary>
		int UpdatePumpInFlight { get; set; }

		/// <summary>
		/// Next scheduled UTC time for invitation cleanup.
		/// </summary>
		DateTime NextInvitationSweepUtc { get; set; }

		/// <summary>
		/// Tracks next-allowed timestamps per connection-operation key.
		/// </summary>
		ConcurrentDictionary<long, DateTime> NextAllowedIngressUtcByKey { get; }

		/// <summary>
		/// Tracks in-flight ingress operation keys.
		/// </summary>
		ConcurrentDictionary<long, byte> IngressInFlightByKey { get; }

		/// <summary>
		/// Next UTC timestamp when ingress cleanup sweep is allowed.
		/// </summary>
		DateTime NextIngressSweepUtc { get; set; }
	}
}