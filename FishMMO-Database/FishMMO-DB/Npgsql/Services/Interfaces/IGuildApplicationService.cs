using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for the guild recruitment directory and its application queue.
	/// </summary>
	public interface IGuildApplicationService
	{
		/// <summary>
		/// Submits an application, refusing duplicates, non-recruiting guilds and full guilds.
		/// </summary>
		/// <param name="guildId">Guild applied to.</param>
		/// <param name="characterId">Applying character.</param>
		/// <param name="message">Applicant message.</param>
		/// <param name="maxCapacity">Guild member cap.</param>
		/// <param name="maxPendingPerCharacter">Most outstanding applications one character may hold.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A result indicating success or the reason for refusal.</returns>
		/// <remarks>
		/// Every one of those conditions is tested inside the INSERT. Checking them in application
		/// code and inserting afterwards is a time-of-check-to-time-of-use gap the applicant
		/// controls the timing of: apply to a guild with one seat left, twice, from two clients.
		/// </remarks>
		Task<DatabaseResult> ApplyAsync(long guildId, long characterId, string message, int maxCapacity, int maxPendingPerCharacter, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches the pending applications for one guild, oldest first.
		/// </summary>
		/// <param name="guildId">Guild ID.</param>
		/// <param name="limit">Maximum rows to return.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The pending queue.</returns>
		Task<DatabaseResult<IReadOnlyList<GuildApplicationData>>> FetchManyAsync(long guildId, int limit, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches one application by ID.
		/// </summary>
		/// <param name="applicationId">Application ID.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The application, or null.</returns>
		Task<DatabaseResult<GuildApplicationData?>> FetchAsync(long applicationId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes one application by ID.
		/// </summary>
		/// <param name="applicationId">Application ID.</param>
		/// <param name="guildId">The guild the caller believes the application belongs to.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>True when a row was actually removed.</returns>
		/// <remarks>
		/// The guild is part of the WHERE clause, not merely validated beforehand: it is the last
		/// line that stops an officer of one guild resolving another guild's application by ID.
		/// The boolean result is what the accept path uses to claim the application exactly once
		/// — two officers pressing Accept simultaneously, only one of whom gets <c>true</c>.
		/// </remarks>
		Task<DatabaseResult<bool>> DeleteAsync(long applicationId, long guildId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes every application a character has outstanding.
		/// </summary>
		/// <param name="characterId">Character ID.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The number of rows removed.</returns>
		/// <remarks>
		/// Run when a character joins any guild. An application that outlives the applicant's
		/// guildless state is an accept waiting to fail.
		/// </remarks>
		Task<DatabaseResult<int>> DeleteManyByCharacterAsync(long characterId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Searches the recruitment directory.
		/// </summary>
		/// <param name="searchTerm">Optional case-insensitive term matched against name, blurb and tags.</param>
		/// <param name="limit">Maximum rows to return.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>Matching recruiting guilds with their current member counts.</returns>
		Task<DatabaseResult<IReadOnlyList<GuildDirectoryEntryData>>> SearchDirectoryAsync(string searchTerm, int limit, CancellationToken cancellationToken = default);
	}
}
