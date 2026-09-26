using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing character quests.
	/// </summary>
	/// <remarks>
	/// Quest persistence and deletion should be version-gated via the logical <c>Version</c>
	/// so stale updates are rejected and newer authoritative updates win.
	/// </remarks>
	public interface ICharacterQuestService :
		IPersistManyAction<CharacterQuestData>,
		IPersistManyOwnedAction<CharacterQuestData>,
		IFetchCollectionByKeyAction<long, CharacterQuestData>
	{
		/// <summary>
		/// Soft-deletes a specific quest for a character, gated by version.
		/// Used when a quest is turned in or abandoned.
		/// </summary>
		/// <param name="characterId">Character who owns the quest.</param>
		/// <param name="templateId">Quest template ID to delete.</param>
		/// <param name="incomingVersion">Only deletes if this version exceeds the stored version.</param>
		/// <param name="cancellationToken">Optional cancellation token.</param>
		Task<DatabaseResult> DeleteQuestAsync(long characterId, int templateId, long incomingVersion, CancellationToken cancellationToken = default);

		/// <summary>
		/// <see cref="DeleteQuestAsync"/>, admitted only while the writer still holds the character's
		/// session claim.
		/// </summary>
		/// <remarks>
		/// The turn-in or abandon a resident character makes. The claim is captured with the request
		/// and checked, under the character's share lock, in the delete's own transaction — see
		/// <c>CharacterWriteGate</c> — so a delete captured by a session released a moment later
		/// cannot land over the next owner's quest log.
		/// </remarks>
		/// <param name="characterId">Character who owns the quest.</param>
		/// <param name="templateId">Quest template ID to delete.</param>
		/// <param name="incomingVersion">Only deletes if this version exceeds the stored version.</param>
		/// <param name="claim">The claim the delete was captured under. Must name <paramref name="characterId"/>.</param>
		/// <param name="cancellationToken">Optional cancellation token.</param>
		/// <returns>
		/// Success when the quest is deleted. Fails with <see cref="DatabaseErrorCodes.Forbidden"/> when
		/// the claim is no longer held.
		/// </returns>
		Task<DatabaseResult> DeleteQuestOwnedAsync(long characterId, int templateId, long incomingVersion, CharacterSessionLeaseData claim, CancellationToken cancellationToken = default);
	}
}