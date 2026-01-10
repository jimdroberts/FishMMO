using System;
using System.Collections.Generic;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for party membership tracking and invitation state.
	/// Manages all party runtime state separately from PartySystem logic.
	/// </summary>
	public class PartyRuntimeData : RuntimeDataContainer, IPartyRuntimeData
	{
		/// <summary>
		/// Tracks all party members for parties with at least one member logged into this server.
		/// Key: Party ID, Value: Set of Character IDs.
		/// </summary>
		public Dictionary<long, HashSet<long>> PartyMemberTracker { get; private set; }

		/// <summary>
		/// Tracks currently online party members on this scene server.
		/// Key: Party ID, Value: Set of Character IDs.
		/// </summary>
		public Dictionary<long, HashSet<long>> PartyCharacterTracker { get; private set; }

		/// <summary>
		/// Tracks pending party invitations.
		/// Key: FromCharacterID, Value: ToCharacterID.
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
			PartyMemberTracker = new Dictionary<long, HashSet<long>>();
			PartyCharacterTracker = new Dictionary<long, HashSet<long>>();
			PendingInvitations = new Dictionary<long, long>();
			LastFetchTime = DateTime.UtcNow;
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all party tracking data.
		/// </summary>
		public override void Clear()
		{
			PartyMemberTracker?.Clear();
			PartyCharacterTracker?.Clear();
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