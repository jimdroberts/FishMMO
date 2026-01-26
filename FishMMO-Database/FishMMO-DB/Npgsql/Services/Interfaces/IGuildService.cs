using System;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for guild management operations.
	/// Provides async methods for guild creation, deletion, and retrieval.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Write operations (Create*, Delete*) in this service use execution strategies to ensure transient
	/// database failures are automatically retried according to the retry policy configured on the DbContext.
	/// This is critical because SaveChangesAsync and ExecuteSqlRawAsync do not automatically
	/// benefit from EnableRetryOnFailure without manual wrapping.
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
	/// All name lookups are case-insensitive using ToUpper() for consistency.
	/// </para>
	/// </remarks>
	public interface IGuildService
	{
		/// <summary>
		/// Checks if a guild exists by name (case-insensitive).
		/// </summary>
		/// <param name="name">Guild name.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing true if guild exists, false if not found,
		/// or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ (AnyAsync with AsNoTracking) and automatically benefits from
		/// the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// Uses case-insensitive comparison via ToUpper().
		/// </remarks>
		Task<DatabaseResult<bool>> ExistsAsync(string name, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gets the name of a guild by ID.
		/// </summary>
		/// <param name="guildId">Guild ID.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the guild name (or empty string if not found) on success,
		/// or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ (FirstOrDefaultAsync with AsNoTracking) and automatically benefits from
		/// the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// </remarks>
		Task<DatabaseResult<string>> GetNameByIdAsync(long guildId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Creates a new guild if name is available (case-insensitive check).
		/// Uses idempotency protection to ensure guild is created exactly once even on retry.
		/// </summary>
		/// <param name="name">Guild name.</param>
		/// <param name="requestId">Unique request identifier for idempotency protection.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the guild ID (or null on validation failure) on success,
		/// or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// Uses ExecuteIdempotentAsync to ensure transient database failures and client retries
		/// do not result in duplicate guild creation. The requestId must be provided by the caller
		/// and should be stable across retries of the same logical operation.
		/// </remarks>
		Task<DatabaseResult<long?>> CreateAsync(string name, Guid requestId, CancellationToken cancellationToken = default);
		/// Deletes a guild by ID using atomic DELETE operation.
		/// </summary>
		/// <param name="guildId">Guild ID to delete.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult"/> indicating success or containing a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// Uses ExecuteSqlRawAsync with execution strategy wrapping to ensure transient database
		/// failures are automatically retried. Returns success even if guild doesn't exist (idempotent).
		/// </remarks>
		Task<DatabaseResult> DeleteAsync(long guildId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Loads a guild by name (case-insensitive).
		/// </summary>
		/// <param name="name">Guild name.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the guild data (or null if not found) on success,
		/// or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ (FirstOrDefaultAsync with AsNoTracking) and automatically benefits from
		/// the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// Uses case-insensitive comparison via ToUpper().
		/// </remarks>
		Task<DatabaseResult<GuildData?>> LoadByNameAsync(string name, CancellationToken cancellationToken = default);

		/// <summary>
		/// Loads a guild by ID.
		/// </summary>
		/// <param name="guildId">Guild ID.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// A <see cref="DatabaseResult{T}"/> containing the guild data (or null if not found) on success,
		/// or a <see cref="DatabaseException"/> on failure.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ (FirstOrDefaultAsync with AsNoTracking) and automatically benefits from
		/// the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// </remarks>
		Task<DatabaseResult<GuildData?>> LoadByIdAsync(long guildId, CancellationToken cancellationToken = default);
	}
}