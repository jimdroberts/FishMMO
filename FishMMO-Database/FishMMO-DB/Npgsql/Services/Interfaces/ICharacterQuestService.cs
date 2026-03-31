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
	}
}