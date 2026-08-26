using System.Threading.Tasks;
using FishMMO.Shared;
using FishMMO.Shared.Core;

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

		/// <summary>
		/// Adds a character to an existing party without an invitation, so that joining another
		/// group's dungeon instance also joins that group.
		/// </summary>
		/// <remarks>
		/// Not a general-purpose membership API and not an invitation bypass. The only intended
		/// caller is the dungeon finder, which reaches it only after establishing that the party
		/// has published a joinable instance — an explicit and revocable offer by its leader — and
		/// only for a character who has no party of their own to be removed from.
		/// <para>
		/// Enforces the same party size limit the invitation path does, so a full party's
		/// instance is simply not joinable.
		/// </para>
		/// </remarks>
		/// <param name="conn">The joining character's connection.</param>
		/// <param name="characterID">The joining character.</param>
		/// <param name="partyID">Party that owns the instance being joined.</param>
		/// <param name="healthPCT">Current health fraction, for the party roster.</param>
		/// <returns>True when membership was persisted, or the character was already a member.</returns>
		Task<bool> TryAddCharacterToPartyAsync(TConnection conn, long characterID, long partyID, float healthPCT);

		/// <summary>
		/// Forms a party of one for a character opening a dungeon instance others may join.
		/// </summary>
		/// <remarks>
		/// An instance is owned by a party and joining one joins that party, so an instance opened
		/// by an ungrouped character has no group for a joiner to be added to. Rather than
		/// refusing to let ungrouped players advertise a run at all, choosing to open one publicly
		/// forms the party that listing implies. A private or solo run creates nothing.
		/// </remarks>
		/// <param name="conn">The character's connection.</param>
		/// <param name="characterID">The character forming the party.</param>
		/// <param name="worldServerID">World server the party will belong to.</param>
		/// <param name="sceneName">Scene name, for the create broadcast's location field.</param>
		/// <param name="healthPCT">Current health fraction, for the party roster.</param>
		/// <returns>The new party ID, or 0 when it could not be created.</returns>
		Task<long> TryCreatePartyForInstanceAsync(TConnection conn, long characterID, long worldServerID, string sceneName, float healthPCT);

		/// <summary>
		/// Drops a character out of a party it cannot belong to, with no connection involved.
		/// </summary>
		/// <remarks>
		/// For a character that is still loading and has no spawned object to broadcast to —
		/// principally one that has arrived on a world server other than the one its party belongs
		/// to. Parties are replicated by a pump scoped to a single world server, so a membership
		/// that crossed would never converge.
		/// <para>
		/// The character's rank is not a parameter. It is re-read from the membership row, because
		/// every caller's copy of it comes from <c>IPartyController.Rank</c> — a cache the update
		/// pump refreshes — and whether leadership has to move is decided from that rank.
		/// </para>
		/// </remarks>
		/// <param name="characterID">Character being removed.</param>
		/// <param name="partyID">Party it is being removed from.</param>
		/// <param name="reason">Why, for the log line.</param>
		/// <returns>
		/// True when the character no longer belongs to the party — including when it never did.
		/// False means the removal could not be attempted and the membership row still stands, so
		/// a caller that was clearing the way for something else must not proceed.
		/// </returns>
		Task<bool> RemoveCharacterFromPartyAsync(long characterID, long partyID, string reason);
	}
}