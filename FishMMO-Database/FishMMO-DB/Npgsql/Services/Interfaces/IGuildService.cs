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
		/// A <see cref="DatabaseResult{T}"/> containing the guild name on success,
		/// or <c>null</c> if the guild was not found.
		/// Returns a failure result on database errors.
		/// </returns>
		/// <remarks>
		/// This method uses LINQ (FirstOrDefaultAsync with AsNoTracking) and automatically benefits from
		/// the retry policy configured on the DbContext without requiring explicit execution strategy wrapping.
		/// </remarks>
		Task<DatabaseResult<string?>> FetchNameAsync(long guildId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates the message of the day for a guild.
		/// </summary>
		/// <param name="guildId">Guild ID.</param>
		/// <param name="messageOfTheDay">The new message of the day text.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>DatabaseResult indicating success or failure.</returns>
		Task<DatabaseResult> PersistMessageOfTheDayAsync(long guildId, string messageOfTheDay, CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates the notice text for a guild.
		/// </summary>
		/// <param name="guildId">Guild ID.</param>
		/// <param name="notice">The new notice text.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>DatabaseResult indicating success or failure.</returns>
		/// <remarks>
		/// The sibling of <see cref="PersistMessageOfTheDayAsync"/>. The <c>notice</c> column has
		/// existed since the guild table was created with no way to write it; the two are kept
		/// separate because a notice is standing text about the guild while the message of the day
		/// is transient, and a single setter would force callers to read-modify-write the other.
		/// </remarks>
		Task<DatabaseResult> PersistNoticeAsync(long guildId, string notice, CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates the guild's recruitment advertisement.
		/// </summary>
		/// <param name="guildId">Guild ID.</param>
		/// <param name="blurb">Advertisement text shown to non-members.</param>
		/// <param name="tags">Comma-separated tags; stored lower-cased for search.</param>
		/// <param name="isRecruiting">Whether the guild is listed in the directory.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>DatabaseResult indicating success or failure.</returns>
		/// <remarks>
		/// One setter for all three fields rather than three. They are edited together from one
		/// form, and separate setters would make "stop recruiting" a two-write operation with a
		/// window in which the guild is listed with a blurb it has just withdrawn.
		/// </remarks>
		Task<DatabaseResult> PersistRecruitmentAsync(long guildId, string blurb, string tags, bool isRecruiting, CancellationToken cancellationToken = default);
	}
}