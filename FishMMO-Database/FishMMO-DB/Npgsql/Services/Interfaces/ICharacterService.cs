using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for managing character entities in the database.
	/// Handles core character operations including creation, retrieval, updates, and deletion.
	/// </summary>
	/// <remarks>
	/// <para>
	/// All write operations (Create*, Persist*, Delete*, Set*, Update*) in this service use execution strategies
	/// to ensure transient database failures are automatically retried according to the retry policy configured
	/// on the DbContext. This is critical because ExecuteSqlRawAsync and SaveChangesAsync do not
	/// automatically retry on transient failures without an execution strategy wrapper.
	/// BaseService provides execution wrappers for retry and centralized exception mapping;
	/// explicit transactions are used only when a write requires multiple database statements.
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
	/// Write operations prefer single-statement SQL (UPDATE/INSERT/DELETE, UPSERT/CTEs) to preserve atomicity
	/// and avoid race conditions when multiple servers or clients modify data simultaneously. When more than
	/// one database statement is unavoidable, the implementation uses an explicit transaction wrapper.
	/// </para>
	/// </remarks>
	public interface ICharacterService :
		ICountByKeyAction<string>,
		IDeleteByKeyVersionedAction<long>,
		IFetchByKeyAction<long, CharacterData?>,
		IFetchByKeyAction<string, CharacterData?>,
		IFetchManyByKeyAction<string, CharacterData>,
		IPersistAction<CharacterData>
	{
		/// <summary>
		/// Creates a new character in the database.
		/// </summary>
		/// <param name="characterData">The character data to create.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the newly inserted character ID on success,
		/// or a failure with <see cref="DatabaseErrorCodes.AlreadyExists"/> if the name is taken,
		/// <see cref="DatabaseErrorCodes.ValidationError"/> for invalid input,
		/// or <see cref="DatabaseErrorCodes.DatabaseError"/> for unexpected failures.
		/// </returns>
		/// <remarks>
		/// Uses a single-statement SQL insert (CTE-based) with execution strategy wrapping
		/// to ensure transient database failures are automatically retried.
		/// Character names are stored with a lowercase version for case-insensitive uniqueness.
		/// </remarks>
		Task<DatabaseResult<long>> CreateCharacterAsync(CharacterData characterData, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches a character by name with an optional selected filter.
		/// </summary>
		/// <param name="characterName">The character name.</param>
		/// <param name="selected">If provided, filters by the selected status.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the character data, or null if not found.
		/// </returns>
		Task<DatabaseResult<CharacterData?>> FetchAsync(string characterName, bool? selected, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches a character by account name. Returns the first matching character.
		/// </summary>
		/// <param name="accountName">The account name.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the character data, or null if not found.
		/// </returns>
		Task<DatabaseResult<CharacterData?>> FetchByAccountAsync(string accountName, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches a character by account name with an optional selected filter.
		/// </summary>
		/// <param name="accountName">The account name.</param>
		/// <param name="selected">If provided, filters by the selected status.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the character data, or null if not found.
		/// </returns>
		Task<DatabaseResult<CharacterData?>> FetchByAccountAsync(string accountName, bool? selected, CancellationToken cancellationToken = default);

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
		/// Attempts to claim ownership of a character session (Offline → Online).
		/// </summary>
		/// <remarks>
		/// A claim is permitted if the character is offline or the previous owner's lease has expired.
		/// On success, returns a new session owner token that must be presented for subsequent operations.
		/// </remarks>
		Task<DatabaseResult<Guid>> TryClaimAsync(long characterId, long ownerServerId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Releases an online character back to offline and clears ownership in a single step.
		/// </summary>
		Task<DatabaseResult> ReleaseAsync(long characterId, long ownerServerId, Guid ownerToken, CancellationToken cancellationToken = default);

		/// <summary>
		/// Refreshes the session lease for an owned online character.
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
		/// Updates the routing information for a character atomically.
		/// </summary>
		/// <param name="characterId">The character ID.</param>
		/// <param name="worldServerId">The world server the character is being routed through.</param>
		/// <param name="sceneName">The scene name.</param>
		/// <param name="sceneHandle">The scene handle.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult"/> indicating success or containing a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// Uses atomic UPDATE to set world_server_id, scene_name and scene_handle in one operation.
		/// All three are written together because the Scene Server matches an incoming character
		/// against its loaded scene instances on the full (world_server_id, scene_name, scene_handle)
		/// tuple — persisting the scene half while leaving world_server_id stale makes that lookup
		/// reject the character as mismatched.
		/// Updates last_saved timestamp automatically.
		/// Execution strategy wrapping ensures transient database failures are automatically retried.
		/// </remarks>
		Task<DatabaseResult> UpdateSceneAsync(long characterId, long worldServerId, string sceneName, int sceneHandle, CancellationToken cancellationToken = default);

		/// <summary>
		/// Retrieves the selected character for each of the specified accounts in batches.
		/// </summary>
		/// <param name="accounts">List of account names to query.</param>
		/// <param name="maxBatchSize">Maximum number of accounts per database round-trip (500–2500).</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A list of selected CharacterData, one per account that has a selected character.</returns>
		Task<DatabaseResult<IReadOnlyList<CharacterData>>> FetchSelectedCharactersByAccountsAsync(List<string> accounts, int maxBatchSize = 1000, CancellationToken cancellationToken = default);

		/// <summary>
		/// Checks whether any non-deleted character on the given account is currently online.
		/// </summary>
		/// <param name="account">The account name.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing <c>true</c> if at least one character is online,
		/// <c>false</c> otherwise.
		/// </returns>
		Task<DatabaseResult<bool>> AnyOnlineAsync(string account, CancellationToken cancellationToken = default);
	}
}