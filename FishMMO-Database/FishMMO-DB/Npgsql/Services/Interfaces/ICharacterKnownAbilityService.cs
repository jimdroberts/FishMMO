using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service interface for managing character known abilities in the database.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	public interface ICharacterKnownAbilityService
	{
		/// <summary>
		/// Saves a known ability for a character.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="templateId">The ability template ID.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or containing error details.
		/// </returns>
		Task<DatabaseResult> SaveKnownAbilityAsync(long characterId, int templateId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Saves multiple known abilities.
		/// </summary>
		/// <param name="knownAbilities">Collection of known ability data to save.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or containing error details.
		/// </returns>
		Task<DatabaseResult> SaveKnownAbilitiesAsync(IEnumerable<CharacterKnownAbilityData> knownAbilities, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a specific known ability for a character.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="templateId">The ability template ID.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or containing error details.
		/// </returns>
		Task<DatabaseResult> DeleteKnownAbilityAsync(long characterId, int templateId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes all known abilities for a specific character.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or containing error details.
		/// </returns>
		Task<DatabaseResult> DeleteAllKnownAbilitiesAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves all known abilities for a specific character.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// DatabaseResult containing a read-only list of character known ability data on success,
		/// or error details on failure.
		/// </returns>
		Task<DatabaseResult<IReadOnlyList<CharacterKnownAbilityData>>> GetKnownAbilitiesAsync(long characterId, CancellationToken cancellationToken = default);
	}
}