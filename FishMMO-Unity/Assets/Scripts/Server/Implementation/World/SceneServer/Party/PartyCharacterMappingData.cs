using System.Collections.Generic;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for party member and character tracking.
	/// Manages party membership lookups separately from PartySystem logic.
	/// </summary>
	public class PartyCharacterMappingData : RuntimeDataContainer, IPartyCharacterMappingData
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
		/// Initializes the party character mapping data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			PartyMemberTracker = new Dictionary<long, HashSet<long>>();
			PartyCharacterTracker = new Dictionary<long, HashSet<long>>();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all party character mapping data.
		/// </summary>
		public override void Clear()
		{
			PartyMemberTracker?.Clear();
			PartyCharacterTracker?.Clear();
		}

		/// <summary>
		/// Deinitializes the party character mapping data container.
		/// </summary>
		public override void Deinitialize()
		{
			Clear();
		}
	}
}