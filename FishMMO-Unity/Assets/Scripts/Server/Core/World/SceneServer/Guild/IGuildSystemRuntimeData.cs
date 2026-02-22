using System;

namespace FishMMO.Server.Core.World.SceneServer
{
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
		/// <param name="guildID">Resolved guild identifier when found.</param>
		/// <returns>True when a pending invitation exists; otherwise false.</returns>
		bool TryGetPendingInvitation(long targetCharacterID, out long guildID);

		/// <summary>
		/// Attempts to add a new pending invitation.
		/// </summary>
		/// <param name="targetCharacterID">Invited target character identifier.</param>
		/// <param name="guildID">Inviting guild identifier.</param>
		/// <param name="nowUtc">Current UTC timestamp.</param>
		/// <returns>True when inserted; false when the invitation already exists.</returns>
		bool TryAddPendingInvitation(long targetCharacterID, long guildID, DateTime nowUtc);

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