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
		/// <summary>
		/// Resolves display names for a set of character IDs in one query.
		/// </summary>
		/// <remarks>
		/// For labelling rows in lists shown to other players — the dungeon finder's instance
		/// list, principally. Projects to ID and name only, and is bounded internally as well as
		/// by its caller, because it answers a request a client controls the timing of.
		/// <para>
		/// IDs that do not resolve are simply absent from the result rather than being reported;
		/// a caller showing a list has to cope with a missing name anyway, since a character can
		/// be deleted between the list being built and the names being read.
		/// </para>
		/// </remarks>
		/// <param name="characterIds">Characters to resolve. Duplicates and non-positive IDs are ignored.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>One entry per resolved character, in no particular order.</returns>
		Task<DatabaseResult<IReadOnlyList<CharacterNameData>>> FetchNamesAsync(
			IReadOnlyList<long> characterIds,
			CancellationToken cancellationToken = default);

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
		/// Returns the account's character that currently holds a session, if any.
		/// </summary>
		/// <remarks>
		/// Unlike <see cref="AnyOnlineAsync"/> this counts combat-logout bodies as well, because
		/// the caller needs to know a body exists in order to refuse switching away from it. An
		/// account may only ever have one character in the world, so at most one row matches.
		/// </remarks>
		/// <param name="account">Account to inspect.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The in-world character, or <c>null</c> when the account has none.</returns>
		Task<DatabaseResult<CharacterData?>> FetchInWorldCharacterAsync(string account, CancellationToken cancellationToken = default);

		/// <summary>
		/// Clears the combat-logout flag on a character.
		/// </summary>
		/// <remarks>
		/// Used when the scene server that held a character's body is judged to be gone, so the
		/// character is no longer waiting for a body that will never be handed back.
		/// </remarks>
		/// <param name="characterId">Character to clear.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult> ClearCombatLoggedAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Atomically claims a character's channel-switch cooldown window: succeeds and stamps
		/// the character only if it has not switched within <paramref name="cooldown"/>.
		/// </summary>
		/// <remarks>
		/// A channel switch releases the character and drops the connection, so the client comes
		/// back through the world server on a fresh connection id — very possibly to a different
		/// scene server. Any cooldown held in memory is therefore erased by the switch itself,
		/// which left the limit applying only to switches that were refused. The character row
		/// is the only state that survives the hop.
		/// <para>
		/// Check and stamp are one statement so two scene servers cannot both conclude the
		/// cooldown has elapsed. Deliberately not version-gated: this is a rate limit, not
		/// gameplay state, and it must not lose to — or interfere with — a concurrent save.
		/// </para>
		/// </remarks>
		/// <param name="characterId">Character attempting the switch.</param>
		/// <param name="cooldown">Minimum interval between switches.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// The timestamp the claim replaced when the switch may proceed, so a caller that then
		/// fails to perform the transfer can put it back with
		/// <see cref="RollbackChannelSwitchAsync"/>; <c>null</c> when the character is still on
		/// cooldown and nothing was stamped.
		/// </returns>
		Task<DatabaseResult<DateTime?>> TryBeginChannelSwitchAsync(long characterId, TimeSpan cooldown, CancellationToken cancellationToken = default);

		/// <summary>
		/// Restores a channel-switch cooldown claimed by <see cref="TryBeginChannelSwitchAsync"/>
		/// for a switch that did not happen.
		/// </summary>
		/// <remarks>
		/// The claim has to be taken before the transfer, because it is the last thing that can
		/// refuse the request — but the transfer can still fail after it: the character enters
		/// combat during the validation, the connection goes away, or the scene server's
		/// main-thread queue rejects the hand-off. Leaving the claim in place then charged a player
		/// the full cooldown for a switch they were refused, and answered their retry with "you are
		/// travelling too often" on top of the refusal they had already been given.
		/// <para>
		/// Restores the exact previous value rather than clearing the column, so a player who
		/// genuinely switched moments ago still serves out the remainder of that cooldown. Nothing
		/// else writes this column, and a character has one session, so the guard below cannot
		/// discard a newer legitimate claim.
		/// </para>
		/// </remarks>
		/// <param name="characterId">Character whose claim is being released.</param>
		/// <param name="previousUtc">The value returned by the claim, restored as-is.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		Task<DatabaseResult> RollbackChannelSwitchAsync(long characterId, DateTime previousUtc, CancellationToken cancellationToken = default);

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
		/// Refreshes the session lease for many owned online characters in a single round trip.
		/// </summary>
		/// <remarks>
		/// Session liveness must not depend on save throughput. Refreshing one character per
		/// round trip inside the periodic save loop meant that on a busy shard with a slow
		/// database the characters at the tail of the loop could exceed the lease duration
		/// between refreshes and become claimable while still online. This performs the whole
		/// population in one statement, so the cost is independent of how many characters are
		/// resident.
		/// <para>
		/// Each entry is verified against the stored owner server and token, so a server that
		/// no longer owns a session silently refreshes nothing rather than extending the
		/// current owner's lease.
		/// </para>
		/// </remarks>
		/// <param name="leases">Ownership triples to refresh. Invalid entries are skipped.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The number of leases actually refreshed.</returns>
		Task<DatabaseResult<int>> RefreshSessionLeasesAsync(IReadOnlyList<CharacterSessionLeaseData> leases, CancellationToken cancellationToken = default);

		/// <summary>
		/// Persists a character row, requiring that the caller still holds its session claim.
		/// </summary>
		/// <remarks>
		/// The claim taken by <see cref="TryClaimAsync"/> gates who may <em>load</em> a
		/// character. Without this method nothing gated who may <em>write</em> one: the plain
		/// <c>PersistAsync</c> is guarded only by the monotonic <c>Version</c>, so a server
		/// whose lease lapsed while it was still running kept saving a character another server
		/// had legitimately claimed — and reliably won, because its version counter had been
		/// climbing for the whole session while the new owner started again from the persisted
		/// row. The claim was advisory on the write path, which is what made a lease lapse
		/// corrupting rather than merely untidy.
		/// <para>
		/// Ownership is verified in the same statement as the write, so there is no window
		/// between checking and writing. A caller that no longer owns the row gets
		/// <see cref="DatabaseErrorCodes.Forbidden"/> and must stop simulating the character
		/// rather than retrying — the current owner's state is authoritative, and replaying a
		/// stale snapshot over it would destroy exactly the progress this refuses to overwrite.
		/// </para>
		/// </remarks>
		/// <param name="characterData">Snapshot to persist. Its <c>Version</c> must exceed the stored version.</param>
		/// <param name="ownership">The claim this server holds, as returned by <see cref="TryClaimAsync"/>.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// Success when written; <see cref="DatabaseErrorCodes.Forbidden"/> when the claim is no
		/// longer held; <see cref="DatabaseErrorCodes.StaleState"/> when a newer version is
		/// already stored; <see cref="DatabaseErrorCodes.NotFound"/> when the row is gone.
		/// </returns>
		Task<DatabaseResult> PersistOwnedAsync(CharacterData characterData, CharacterSessionLeaseData ownership, CancellationToken cancellationToken = default);

		/// <summary>
		/// Returns the subset of <paramref name="leases"/> whose sessions the database no longer
		/// attributes to the supplied owner — that is, the claims the caller has lost.
		/// </summary>
		/// <remarks>
		/// <see cref="RefreshSessionLeasesAsync"/> reports only how many rows it refreshed, so a
		/// short count says a claim was lost without saying which. This resolves that, and is
		/// meant to be called only on the short-count path: it is a diagnostic read, not part of
		/// the refresh hot path.
		/// <para>
		/// A character reported here is being simulated by a server that can no longer persist
		/// it. The caller must evict it locally; see <see cref="PersistOwnedAsync"/>.
		/// </para>
		/// </remarks>
		/// <param name="leases">Ownership triples the caller believes it holds.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Character IDs no longer owned by the supplied server/token, including rows that have been deleted.</returns>
		Task<DatabaseResult<IReadOnlyList<long>>> FetchUnownedSessionsAsync(IReadOnlyList<CharacterSessionLeaseData> leases, CancellationToken cancellationToken = default);

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
		Task<DatabaseResult> UpdateSceneAsync(long characterId, long worldServerId, string sceneName, long sceneHandle, CancellationToken cancellationToken = default);

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