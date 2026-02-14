using System;
using System.Collections.Generic;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime data container for party system state.
	/// Provides read-only access to party invitations and database synchronization state.
	/// </summary>
	public interface IPartySystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>
		/// Tracks pending party invitations.
		/// Key: TargetCharacterID (invited character), Value: PartyID (party being invited to).
		/// </summary>
		Dictionary<long, long> PendingInvitations { get; }

		/// <summary>
		/// Timestamp of the last successful database fetch for party updates.
		/// </summary>
		DateTime LastFetchTime { get; set; }
	}
}