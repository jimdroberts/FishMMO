using FishMMO.Shared;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Engine-agnostic public API for guild management on a scene server.
	/// Exposes the small surface other systems use to track guild membership and
	/// react to guild lifecycle events without referencing engine-specific types.
	/// </summary>
	public interface IGuildSystem<TConnection> : IServerBehaviour
	{
		/// <summary>
		/// Maximum members allowed in a guild on this server.
		/// </summary>
		int MaxGuildSize { get; }

		/// <summary>
		/// Maximum allowed length for guild names.
		/// </summary>
		int MaxGuildNameLength { get; }

		/// <summary>
		/// Pump/update rate (in seconds) used by the guild system for polling/sync.
		/// </summary>
		float UpdatePumpRate { get; }

		/// <summary>
		/// Adds a mapping for a guild to a character currently connected to this scene server.
		/// </summary>
		/// <param name="guildID">Guild identifier.</param>
		/// <param name="characterID">Character identifier to add.</param>
		void AddGuildCharacterTracker(long guildID, long characterID);

		/// <summary>
		/// Removes a mapping for a guild to a character on this scene server.
		/// </summary>
		/// <param name="guildID">Guild identifier.</param>
		/// <param name="characterID">Character identifier to remove.</param>
		void RemoveGuildCharacterTracker(long guildID, long characterID);

		/// <summary>
		/// Called by the Character system when a character connects; used to update guild trackers and persist state.
		/// </summary>
		/// <param name="conn">Opaque connection object (engine-specific implementations should accept their connection type).</param>
		/// <param name="character">The character that connected.</param>
		void CharacterSystem_OnConnect(TConnection conn, IPlayerCharacter character);

		/// <summary>
		/// Called by the Character system when a character disconnects; used to update guild trackers and persist state.
		/// </summary>
		/// <param name="conn">Opaque connection object.</param>
		/// <param name="character">The character that disconnected.</param>
		void CharacterSystem_OnDisconnect(TConnection conn, IPlayerCharacter character);

		/// <summary>
		/// Chat command handler used by the chat helper to issue guild invites.
		/// Returns true if the command was handled.
		/// </summary>
		bool OnGuildInvite(IPlayerCharacter sender, ChatBroadcast msg);
	}
}