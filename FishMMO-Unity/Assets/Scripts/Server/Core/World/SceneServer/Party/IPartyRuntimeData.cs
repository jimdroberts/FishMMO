using System;
using System.Collections.Generic;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime data container for party membership tracking and invitation state.
	/// Provides read-only access to party tracking collections.
	/// </summary>
	public interface IPartyRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Tracks all party members for parties with at least one member logged into this server.
		/// Key: Party ID, Value: Set of Character IDs.
		/// </summary>
		Dictionary<long, HashSet<long>> PartyMemberTracker { get; }

		/// <summary>
		/// Tracks currently online party members on this scene server.
		/// Key: Party ID, Value: Set of Character IDs.
		/// </summary>
		Dictionary<long, HashSet<long>> PartyCharacterTracker { get; }

		/// <summary>
		/// Tracks pending party invitations.
		/// Key: FromCharacterID, Value: ToCharacterID.
		/// </summary>
		Dictionary<long, long> PendingInvitations { get; }

		/// <summary>
		/// Timestamp of the last successful database fetch for party updates.
		/// </summary>
		DateTime LastFetchTime { get; set; }
	}
}