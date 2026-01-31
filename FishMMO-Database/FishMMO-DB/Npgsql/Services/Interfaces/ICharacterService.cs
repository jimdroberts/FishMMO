using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service interface for managing character entities in the database.
	/// Handles core character operations including creation, retrieval, updates, and deletion.
	/// </summary>
	/// <remarks>
	/// <para>
	/// All write operations (Create*, Save*, Delete*, Set*, Update*) in this service use execution strategies
	/// to ensure transient database failures are automatically retried according to the retry policy configured
	/// on the DbContext. This is critical because ExecuteSqlRawAsync and SaveChangesAsync do not
	/// Execution is wrapped by BaseService for retries/transactions.
	/// </para>
	/// <para>
	/// All methods return <see cref="DatabaseResult"/> or <see cref="DatabaseResult{T}"/> to provide
	/// structured error information through the DatabaseException system, helping distinguish between:
	/// - Validation failures (invalid parameters)
	/// - Business rule violations (name already exists)
	/// - Database errors (connection issues, constraint violations, timeouts)
	/// - Entity not found errors
	/// - Unexpected runtime errors
	/// </para>
	/// <para>
	/// All SQL operations use atomic UPDATE, INSERT, and DELETE commands to prevent race conditions
	/// when multiple servers or clients modify character data simultaneously.
	/// </para>
	/// </remarks>
	public interface ICharacterService
	{
		/// <summary>
		/// Gets the count of characters for a specific account.
		/// </summary>
		/// <param name="account">The account name.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the character count on success,
		/// or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ (CountAsync with AsNoTracking) and automatically benefits from
		/// the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// </remarks>
		Task<DatabaseResult<int>> GetCountAsync(string account, CancellationToken cancellationToken = default);

		/// <summary>
		/// Creates a new character in the database using SaveChangesAsync.
		/// </summary>
		/// <param name="characterData">The character data to create.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing a CharacterOperationResult on success,
		/// or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// Uses EF Core's SaveChangesAsync for insert operations with execution strategy wrapping
		/// to ensure transient database failures are automatically retried.
		/// Character names are stored with a lowercase version for case-insensitive uniqueness.
		/// </remarks>
		Task<DatabaseResult<CharacterOperationResult>> CreateCharacterAsync(CharacterData characterData, CancellationToken cancellationToken = default);

		/// <summary>
		/// Saves an existing character's data to the database using atomic UPDATE.
		/// </summary>
		/// <param name="characterData">The character data to save.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult"/> indicating success or containing a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// Uses atomic UPDATE operation to save all character fields in one operation.
		/// Updates the last_saved timestamp automatically.
		/// Uses BaseService.ExecuteTransactionAsync for automatic transient failure retry and centralized exception mapping.
		/// </remarks>
		Task<DatabaseResult> SaveCharacterAsync(CharacterData characterData, CancellationToken cancellationToken = default);

		/// <summary>
		/// Soft-deletes a character.
		/// </summary>
		/// <param name="characterId">The character ID to delete.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult"/> indicating success or containing a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// Marks the character and all character-owned rows as deleted (soft cascade), without removing data.
		/// To allow reusing the character name, the character is renamed by appending <c>_DELETED_{GUID}</c>
		/// to <c>Name</c>. <c>NameLowercase</c> is derived from <c>Name</c> (case-insensitive uniqueness).
		/// Character guild/party membership rows are hard-deleted (temporary state).
		/// If the character does not exist (or is already deleted), this method returns success (idempotent).
		/// Execution strategy wrapping ensures transient database failures are automatically retried.
		/// </remarks>
		Task<DatabaseResult> DeleteCharacterAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves a character by its ID.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the character data (or null if not found) on success,
		/// or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		Task<DatabaseResult<CharacterData?>> GetCharacterAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves all characters for a specific account.
		/// </summary>
		/// <param name="account">The account name.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing a list of character data on success,
		/// or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		Task<DatabaseResult<IReadOnlyList<CharacterData>>> GetCharactersAsync(string account, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves a character by its name.
		/// </summary>
		/// <param name="name">The character name (case-insensitive).</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the character data (or null if not found) on success,
		/// or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		Task<DatabaseResult<CharacterData?>> GetCharacterByNameAsync(string name, CancellationToken cancellationToken = default);

		/// <summary>
		/// Sets the selected character for an account atomically. Deselects all other characters for the account.
		/// </summary>
		/// <param name="account">The account name.</param>
		/// <param name="characterId">The character ID to select.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult"/> indicating success or containing a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// Uses a single atomic UPDATE with conditional logic: SET selected = (id = characterId).
		/// This ensures all characters for the account are updated in one operation without race conditions.
		/// Execution strategy wrapping ensures transient database failures are automatically retried.
		/// </remarks>
		Task<DatabaseResult> SetSelectedAsync(string account, long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Sets the online status for a character atomically.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="online">The online status to set.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult"/> indicating success or containing a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// Uses atomic UPDATE without loading the entity. Updates last_saved timestamp automatically.
		/// Execution strategy wrapping ensures transient database failures are automatically retried.
		/// </remarks>
		/// <summary>
		/// Attempts to claim ownership of a character session.
		/// </summary>
		/// <remarks>
		/// A claim is permitted if the character is offline or the previous owner's lease has expired.
		/// On success, returns a new session owner token that must be presented for subsequent transitions.
		/// </remarks>
		Task<DatabaseResult<Guid>> TryClaimAsync(long characterId, long ownerServerId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Marks a character as transitioning, preventing other servers from claiming it.
		/// </summary>
		Task<DatabaseResult> BeginTransitionAsync(long characterId, long ownerServerId, Guid ownerToken, CancellationToken cancellationToken = default);

		/// <summary>
		/// Releases a transitioning character back to offline and clears ownership.
		/// </summary>
		Task<DatabaseResult> ReleaseToOfflineAsync(long characterId, long ownerServerId, Guid ownerToken, CancellationToken cancellationToken = default);

		/// <summary>
		/// Refreshes the session lease for an owned (online/transitioning) character.
		/// </summary>
		Task<DatabaseResult> RefreshSessionLeaseAsync(long characterId, long ownerServerId, Guid ownerToken, CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates the position and rotation of a character atomically.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="x">The X coordinate.</param>
		/// <param name="y">The Y coordinate.</param>
		/// <param name="z">The Z coordinate.</param>
		/// <param name="rotX">The rotation X component.</param>
		/// <param name="rotY">The rotation Y component.</param>
		/// <param name="rotZ">The rotation Z component.</param>
		/// <param name="rotW">The rotation W component.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult"/> indicating success or containing a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// Uses atomic UPDATE to set all position and rotation components in one operation.
		/// Updates last_saved timestamp automatically.
		/// Execution strategy wrapping ensures transient database failures are automatically retried.
		/// </remarks>
		Task<DatabaseResult> UpdatePositionAsync(long characterId, float x, float y, float z, float rotX, float rotY, float rotZ, float rotW, CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates the scene information for a character atomically.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="sceneName">The scene name.</param>
		/// <param name="sceneHandle">The scene handle.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult"/> indicating success or containing a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// Uses atomic UPDATE to set scene_name and scene_handle in one operation.
		/// Updates last_saved timestamp automatically.
		/// Execution strategy wrapping ensures transient database failures are automatically retried.
		/// </remarks>
		Task<DatabaseResult> UpdateSceneAsync(long characterId, string sceneName, int sceneHandle, CancellationToken cancellationToken = default);
	}
}