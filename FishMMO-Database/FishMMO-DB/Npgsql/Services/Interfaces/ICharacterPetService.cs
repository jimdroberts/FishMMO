using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service interface for managing character pets in the database.
	/// </summary>
	/// <remarks>
	/// <para>
	/// All write operations (Save*, Delete*) in this service use execution strategies to ensure
	/// transient database failures are automatically retried according to the retry policy configured
	/// on the DbContext. This is critical because ExecuteSqlInterpolatedAsync and SaveChangesAsync
	/// do not automatically benefit from EnableRetryOnFailure without manual wrapping.
	/// </para>
	/// <para>
	/// All methods return <see cref="DatabaseResult"/> or <see cref="DatabaseResult{T}"/> to provide
	/// structured error information through the DatabaseException system, helping distinguish between:
	/// - Validation failures (invalid parameters)
	/// - Database errors (connection issues, constraint violations, timeouts)
	/// - Entity not found errors
	/// - Unexpected runtime errors
	/// </para>
	/// <para>
	/// All SQL operations use atomic UPDATE, INSERT, and DELETE commands to prevent race conditions
	/// when multiple servers or clients modify pet data simultaneously.
	/// </para>
	/// </remarks>
	public interface ICharacterPetService
	{
		/// <summary>
		/// Saves or updates a character's pet using atomic operations.
		/// </summary>
		/// <param name="pet">The pet data to save.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult"/> indicating success or containing a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// If pet.ID > 0, performs an atomic UPDATE. Otherwise, uses SaveChangesAsync to insert a new pet.
		/// Execution strategy wrapping ensures transient database failures are automatically retried.
		/// </remarks>
		Task<DatabaseResult> SavePetAsync(CharacterPetData pet, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a character's pet.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult"/> indicating success or containing a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// Uses atomic DELETE operation. Returns success even if the pet doesn't exist (idempotent).
		/// Execution strategy wrapping ensures transient database failures are automatically retried.
		/// </remarks>
		Task<DatabaseResult> DeletePetAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves a character's pet.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the pet data (or null if not found) on success,
		/// or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ (AsNoTracking) for optimal read performance and automatically benefits from
		/// the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// </remarks>
		Task<DatabaseResult<CharacterPetData?>> GetPetAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves a character's pet only if it is spawned.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the pet data (or null if not found/not spawned) on success,
		/// or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ (AsNoTracking) with a WHERE clause to filter for spawned pets only.
		/// Automatically benefits from the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// </remarks>
		Task<DatabaseResult<CharacterPetData?>> GetSpawnedPetAsync(long characterId, CancellationToken cancellationToken = default);
	}
}