using System;
using System.Collections.Generic;
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
		/// Tracks pending party invitations.
		/// Key: TargetCharacterID (invited character), Value: PartyID (party being invited to).
		/// </summary>
		public Dictionary<long, long> PendingInvitations { get; private set; }

		/// <summary>
		/// Timestamp of the last successful database fetch for party updates.
		/// </summary>
		public DateTime LastFetchTime { get; set; }

		/// <summary>
		/// Initializes the party runtime data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			PendingInvitations = new Dictionary<long, long>();
			LastFetchTime = DateTime.UtcNow;
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all party runtime data.
		/// </summary>
		public override void Clear()
		{
			PendingInvitations?.Clear();
			LastFetchTime = DateTime.UtcNow;
		}

		/// <summary>
		/// Deinitializes the party runtime data container.
		/// </summary>
		public override void Deinitialize()
		{
			Clear();
		}
	}
}