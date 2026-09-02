using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Service interface for a guild's editable rank ladder.
	/// </summary>
	/// <remarks>
	/// The rank rows are the authority for what a member may do. Nothing in this interface takes a
	/// requester: authorisation is the server's job and it happens before these calls, against the
	/// rows <see cref="FetchManyAsync"/> returns. A storage service that also decided permissions
	/// would be two responsibilities with one test surface.
	/// </remarks>
	public interface IGuildRankService
	{
		/// <summary>
		/// Creates the default rank ladder for a guild if it does not already have one.
		/// </summary>
		/// <param name="guildId">Guild ID.</param>
		/// <param name="defaults">The rows to seed, in any order.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The number of rows actually inserted.</returns>
		/// <remarks>
		/// IDEMPOTENT. Every row is inserted with <c>ON CONFLICT (guild_id, rank_order) DO
		/// NOTHING</c>, so running this against a guild that already has ranks changes nothing and
		/// returns zero. That is what makes it safe to call on every guild read, which is in turn
		/// what makes the migration of existing guilds require no migration step at all: a guild
		/// created before rank rows existed grows them the first time anybody looks at it, with
		/// the permissions its old enum ranks implied.
		/// </remarks>
		Task<DatabaseResult<int>> EnsureDefaultsAsync(long guildId, IReadOnlyList<GuildRankData> defaults, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetches every rank row for a guild, ordered by rank order ascending.
		/// </summary>
		/// <param name="guildId">Guild ID.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>The guild's rank ladder.</returns>
		Task<DatabaseResult<IReadOnlyList<GuildRankData>>> FetchManyAsync(long guildId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Updates one rank's name and permission mask.
		/// </summary>
		/// <param name="guildId">Guild ID.</param>
		/// <param name="rankOrder">The rank position to update.</param>
		/// <param name="name">New display name.</param>
		/// <param name="permissions">New permission bit mask.</param>
		/// <param name="incomingVersion">The authoritative, monotonic version for this update.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A result indicating success or failure.</returns>
		Task<DatabaseResult> UpdateAsync(long guildId, byte rankOrder, string name, long permissions, long incomingVersion, CancellationToken cancellationToken = default);

		/// <summary>
		/// Inserts a new rank at a position, moving everything already at or above it up one rung.
		/// </summary>
		/// <param name="rank">The rank to insert. Its rank order is the POSITION it takes.</param>
		/// <param name="maxRanks">Maximum rank rows one guild may own.</param>
		/// <param name="maxRankOrder">Highest legal rank order, which bounds the shift.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A result indicating success or failure.</returns>
		/// <remarks>
		/// <para><b>Why this inserts rather than fills a hole.</b> A guild is seeded with three
		/// CONTIGUOUS ranks — member 1, officer 2, leader 3 — and a rank may only be created
		/// strictly below the creator's own seat. A leader at order 3 therefore has orders 1 and 2
		/// to choose from and both are taken, so a "create at a free position" service could never
		/// add a rank to a guild that had not already had one removed. Making room is the
		/// operation; finding a gap was never going to find one.</para>
		///
		/// <para><b>Membership rows move with the ladder.</b> <c>character_guild.rank</c> holds the
		/// same order byte, so every member at or above the insertion point is shifted by the same
		/// one rung, in the same transaction. Relative standing is what the whole permission model
		/// compares, and it is preserved exactly: a member who outranked another before the insert
		/// still does, and the leader — defined as the highest order that exists — is still the
		/// same character.</para>
		///
		/// <para><b>The shift is applied one row at a time, highest first.</b>
		/// <c>(guild_id, rank_order)</c> is UNIQUE and PostgreSQL checks it per row rather than at
		/// statement end, so a single <c>SET rank_order = rank_order + 1</c> over the whole range
		/// raises a duplicate key the moment the planner happens to reach a row whose target is
		/// still occupied. Descending, each target has just been vacated. Twelve ranks is the cap,
		/// so this is at most eleven statements for something a guild does once in a while.</para>
		/// </remarks>
		Task<DatabaseResult> InsertAsync(GuildRankData rank, int maxRanks, byte maxRankOrder, CancellationToken cancellationToken = default);

		/// <summary>
		/// Deletes a rank row, refusing while any member still holds it.
		/// </summary>
		/// <param name="guildId">Guild ID.</param>
		/// <param name="rankOrder">The rank position to delete.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		/// <returns>A result indicating success or failure.</returns>
		/// <remarks>
		/// The occupancy test and the delete are ONE statement. Split across two round trips, a
		/// member could be moved into the rank between them and end up holding a rank that no
		/// longer exists — which reads back as no permissions at all, quietly, forever.
		/// </remarks>
		Task<DatabaseResult> DeleteAsync(long guildId, byte rankOrder, CancellationToken cancellationToken = default);
	}
}
