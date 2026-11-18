using System.Collections.Generic;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime data container for party member and character tracking.
	/// Provides read-only access to party membership lookups.
	/// </summary>
	public interface IPartyCharacterMappingData : IRuntimeDataContainer
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
	}
}