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
	/// <remarks>
	/// <para>
	/// All write operations (Save*, Delete*) in this service use execution strategies to ensure
	/// transient database failures are automatically retried according to the retry policy configured
	/// on the DbContext. This is critical because ExecuteSqlInterpolatedAsync and explicit transactions
	/// do not automatically benefit from EnableRetryOnFailure without manual wrapping.
	/// </para>
	/// <para>
	/// All methods return DatabaseResult to provide structured error handling.
	/// Exceptions are caught and wrapped in appropriate DatabaseException types,
	/// allowing callers to distinguish between validation errors, constraint violations,
	/// and transient database failures.
	/// </para>
	/// <para>
	/// All SQL operations use atomic INSERT ON CONFLICT and DELETE commands to prevent race conditions
	/// when multiple servers or clients modify the same character's known abilities simultaneously.
	/// </para>
	/// </remarks>
	public interface ICharacterKnownAbilityService
	{
		/// <summary>
		/// Saves a known ability for a character using an atomic INSERT ON CONFLICT operation.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="templateId">The ability template ID.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or containing error details.
		/// </returns>
		/// <remarks>
		/// Uses PostgreSQL INSERT ON CONFLICT DO NOTHING to ensure idempotent operations.
		/// Execution strategy wrapping ensures transient database failures are automatically retried.
		/// </remarks>
		Task<DatabaseResult> SaveKnownAbilityAsync(long characterId, int templateId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Saves multiple known abilities for a character within a single transaction.
		/// </summary>
		/// <param name="knownAbilities">Collection of known ability data to save.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or containing error details.
		/// </returns>
		/// <remarks>
		/// All abilities are saved within a single transaction for atomicity. If any operation fails,
		/// the entire transaction is rolled back. Uses INSERT ON CONFLICT DO NOTHING for each ability.
		/// Execution strategy wrapping ensures the entire transaction can be retried on transient failures.
		/// </remarks>
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
		/// <remarks>
		/// Uses atomic DELETE operation. Returns success even if the ability doesn't exist (idempotent).
		/// Execution strategy wrapping ensures transient database failures are automatically retried.
		/// </remarks>
		Task<DatabaseResult> DeleteKnownAbilityAsync(long characterId, int templateId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes all known abilities for a specific character.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// DatabaseResult indicating success or containing error details.
		/// </returns>
		/// <remarks>
		/// Uses atomic DELETE operation to remove all abilities for the character in one operation.
		/// Returns success even if no abilities exist (idempotent).
		/// Execution strategy wrapping ensures transient database failures are automatically retried.
		/// </remarks>
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
		/// <remarks>
		/// This method uses LINQ (AsNoTracking) for optimal read performance and automatically benefits from
		/// the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// </remarks>
		Task<DatabaseResult<IReadOnlyList<CharacterKnownAbilityData>>> GetKnownAbilitiesAsync(long characterId, CancellationToken cancellationToken = default);
	}
}