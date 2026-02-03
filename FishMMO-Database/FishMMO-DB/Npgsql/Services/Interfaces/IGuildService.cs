using System;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces.Actions;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for guild management operations.
	/// Provides async methods for guild creation, deletion, and retrieval.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Write operations (Persist*, Delete*) in this service use execution strategies to ensure transient
	/// database failures are automatically retried according to the retry policy configured on the DbContext.
	/// Execution is wrapped by BaseService for retries and exception mapping.
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
	/// Name lookups are case-insensitive by using a normalized field (e.g. name_lowercase) in the database.
	/// </para>
	/// </remarks>
	public interface IGuildService :
		IExistsByKeyAction<string>,
		IPersistAction<string, long?>,
		IDeleteByKeyAction<long>,
		IFetchByKeyAction<long, GuildData?>,
		IFetchByKeyAction<string, GuildData?>
	{
		/// <summary>
		/// Fetches the name of a guild by ID.
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
		Task<DatabaseResult<string>> FetchNameAsync(long guildId, CancellationToken cancellationToken = default);
	}
}