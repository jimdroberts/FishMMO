using System;
using System.Collections.Generic;
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
		/// Fetches the names of a set of guilds in one query.
		/// </summary>
		/// <remarks>
		/// For the scene servers' batched name lookups: every guild name a client asked for in one
		/// frame, resolved together instead of one query per guild (hot-path audit M18). Bounded
		/// internally as well as by its caller, because it answers a request a client controls
		/// the timing of.
		/// </remarks>
		/// <param name="guildIds">Guilds to resolve. Duplicates and non-positive IDs are ignored.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>
		/// The name of each guild found, by ID. A guild that does not exist is absent. A failure
		/// result on database errors.
		/// </returns>
		Task<DatabaseResult<IReadOnlyDictionary<long, string>>> FetchNamesAsync(IReadOnlyList<long> guildIds, CancellationToken cancellationToken = default);

		/// <summary>
		/// Which of the given guilds still exist.
		/// </summary>
		/// <remarks>
		/// For the scene servers' update pump. A disbanded guild's row goes, and its guild_updates row
		/// goes with it by cascade, so the pump — which only ever learned of changes through that row —
		/// never heard that the guild was gone, and its members on every other server kept a guild that
		/// no longer existed (issue #267). The pump asks this instead.
		/// </remarks>
		/// <param name="guildIds">The guilds to look for.</param>
		/// <param name="cancellationToken">Token to cancel the operation.</param>
		/// <returns>The subset of <paramref name="guildIds"/> that still exist.</returns>
		Task<DatabaseResult<IReadOnlyCollection<long>>> FetchExistingIdsAsync(IReadOnlyCollection<long> guildIds, CancellationToken cancellationToken = default);

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