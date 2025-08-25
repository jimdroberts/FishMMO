using FishMMO.Shared;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Engine-agnostic public API for party management on a scene server.
	/// Implementations manage party membership state for characters connected to
	/// this scene server, synchronize party updates with persistence, and notify
	/// local party members of changes.
	/// </summary>
	public interface IPartySystem<TConnection> : IServerBehaviour
	{
		/// <summary>
		/// Register that a character is currently connected to this scene server and
		/// is a member of the specified party. Implementations typically track this
		/// to push party updates only to active members on the server.
		/// </summary>
		/// <param name="partyID">Identifier of the party.</param>
		/// <param name="characterID">Identifier of the character to add.</param>
		void AddPartyCharacterTracker(long partyID, long characterID);

		/// <summary>
		/// Remove the mapping that a character is connected to this scene server for
		/// the given party. If no members remain for a party this method may allow
		/// implementations to drop cached state for that party.
		/// </summary>
		/// <param name="partyID">Identifier of the party.</param>
		/// <param name="characterID">Identifier of the character to remove.</param>
		void RemovePartyCharacterTracker(long partyID, long characterID);

		/// <summary>
		/// Called by the character system when a character connects. Implementations
		/// should use this callback to add the character to party trackers and to
		/// persist or broadcast party state as needed.
		/// </summary>
		/// <param name="conn">Opaque connection object representing the client's connection.</param>
		/// <param name="character">The player character that connected.</param>
		void CharacterSystem_OnConnect(TConnection conn, IPlayerCharacter character);

		/// <summary>
		/// Called by the character system when a character disconnects. Implementations
		/// should remove the character from trackers and persist any party updates.
		/// </summary>
		/// <param name="conn">Opaque connection object.</param>
		/// <param name="character">The player character that disconnected.</param>
		void CharacterSystem_OnDisconnect(TConnection conn, IPlayerCharacter character);
	}
}