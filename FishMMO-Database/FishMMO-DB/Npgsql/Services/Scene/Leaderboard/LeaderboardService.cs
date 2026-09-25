using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Data.Enums;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Read-only leaderboards. See <see cref="ILeaderboardService"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every board is the same three statements over a different FROM clause: the rows of a page,
	/// the count of the whole board, and one character's standing. The FROM/WHERE pair for a
	/// source is built in exactly one place (<see cref="BuildSource"/>) and every statement is
	/// composed from it, so the page, the count and the standing cannot disagree about who is
	/// eligible or what the score is.
	/// </para>
	/// <para>
	/// Parameters are numbered as the fragments are built, so a statement binds exactly the
	/// parameters it references. All of them are <c>int</c> or <c>long</c>; nothing here binds
	/// a <c>uint</c>, which Npgsql cannot.
	/// </para>
	/// </remarks>
	public sealed class LeaderboardService : BaseService<CharacterEntity>, ILeaderboardService
	{
		/// <summary>Largest page the service will return.</summary>
		public const int MaxPageRows = 200;

		/// <summary>Deepest offset the service will read from. A board deeper than this is not browsed by page.</summary>
		public const int MaxOffset = 100_000;

		/// <summary>Initializes a new instance of LeaderboardService.</summary>
		public LeaderboardService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<LeaderboardPageData>> FetchPageAsync(LeaderboardQuery query, int offset, int limit, CancellationToken cancellationToken = default)
		{
			if (!IsValid(query, out string refusal))
			{
				return DatabaseResult<LeaderboardPageData>.Failure(DatabaseErrorCodes.ValidationError, refusal);
			}
			if (offset < 0 || offset > MaxOffset)
			{
				return DatabaseResult<LeaderboardPageData>.Failure(DatabaseErrorCodes.ValidationError, $"Offset must be between 0 and {MaxOffset}.");
			}
			limit = Math.Clamp(limit, 1, MaxPageRows);

			return await ExecuteReadAsync<LeaderboardPageData>(async dbContext =>
			{
				DateTime fetchedAt = DateTime.UtcNow;

				(long seasonId, string seasonName) = (0, string.Empty);
				if (query.Source == LeaderboardSourceKind.ArenaSeasonRating)
				{
					(seasonId, seasonName) = await ReadActiveSeasonAsync(dbContext, cancellationToken).ConfigureAwait(false);
					if (seasonId == 0)
					{
						return new LeaderboardPageData(0, string.Empty, 0, offset, Array.Empty<LeaderboardRowData>(), fetchedAt);
					}
				}

				SourceSql source = BuildSource(dbContext, query, seasonId);

				/* Two windows over one ordering. RANK() is ordered by the score alone, so equal
				 * scores share a rank; ROW_NUMBER() adds the tie-break, giving every row a fixed
				 * position so consecutive pages neither overlap nor skip. The rank ordering is a
				 * prefix of the position ordering, so both windows come from one sorted pass.
				 *
				 * The LIMIT sits INSIDE the ranked subquery, as offset + limit, and that placement is
				 * load-bearing. Written the obvious way (window over the board, then WHERE position >
				 * offset ORDER BY position LIMIT n outside) the planner cannot see that only the top
				 * rows are wanted: measured on 50,000 eligible characters it hash-joined and sorted
				 * the whole board every time, 30-55 ms for page one. With the bound inside, it walks
				 * the (template_id, value) index backwards through nested-loop joins and stops: 1 ms
				 * for page one, 4 ms around rank 1,000, and a deliberate switch back to the full
				 * join only for offsets deep enough that reading everything is cheaper. The outer
				 * ORDER BY then sorts just those rows, so the order returned is guaranteed rather
				 * than inherited from a subquery. */
				int offsetIndex = source.Parameters.Count;
				int boundIndex = offsetIndex + 1;
				string pageSql = $@"SELECT ranked.rank, ranked.character_id, ranked.name, ranked.score, ranked.wins, ranked.losses
					FROM (
						SELECT s.character_id AS character_id, c.name AS name,
							{source.Score} AS score, {source.Wins} AS wins, {source.Losses} AS losses,
							RANK() OVER (ORDER BY {source.Score} DESC) AS rank,
							ROW_NUMBER() OVER (ORDER BY {source.Score} DESC, {source.TieBreak}) AS position
						{source.From}
						WHERE {source.Where}
						ORDER BY {source.Score} DESC, {source.TieBreak}
						LIMIT {{{boundIndex}}}
					) ranked
					WHERE ranked.position > {{{offsetIndex}}}
					ORDER BY ranked.position";

				var pageParameters = new List<object>(source.Parameters) { (long)offset, (long)offset + limit };
				List<LeaderboardRowData> rows = await ReadRowsAsync(dbContext, pageSql, pageParameters.ToArray(), reader => new LeaderboardRowData(
					ClampToInt(reader.GetInt64(0)),
					reader.GetInt64(1),
					reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
					reader.GetInt64(3),
					reader.GetInt32(4),
					reader.GetInt32(5)), cancellationToken).ConfigureAwait(false);

				/* The board's size has no shortcut: it is a count of every eligible row, ~25 ms at
				 * 50,000 characters. Paid once per page read, which the caller caches. */
				string countSql = $@"SELECT COUNT(*) {source.From} WHERE {source.Where}";
				long total = await ExecuteScalarLongAsync(dbContext, countSql, source.Parameters.ToArray(), cancellationToken).ConfigureAwait(false);

				return new LeaderboardPageData(seasonId, seasonName, ClampToInt(total), offset, rows, fetchedAt);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<LeaderboardStandingData>> FetchStandingAsync(LeaderboardQuery query, long characterId, CancellationToken cancellationToken = default)
		{
			if (!IsValid(query, out string refusal))
			{
				return DatabaseResult<LeaderboardStandingData>.Failure(DatabaseErrorCodes.ValidationError, refusal);
			}
			if (characterId <= 0)
			{
				return DatabaseResult<LeaderboardStandingData>.Failure(DatabaseErrorCodes.ValidationError, "Character ID must be greater than zero.");
			}

			return await ExecuteReadAsync<LeaderboardStandingData>(async dbContext =>
			{
				DateTime fetchedAt = DateTime.UtcNow;

				long seasonId = 0;
				if (query.Source == LeaderboardSourceKind.ArenaSeasonRating)
				{
					(seasonId, _) = await ReadActiveSeasonAsync(dbContext, cancellationToken).ConfigureAwait(false);
					if (seasonId == 0)
					{
						return LeaderboardStandingData.Unranked(fetchedAt);
					}
				}

				SourceSql source = BuildSource(dbContext, query, seasonId);

				/* The character's own row under the board's own eligibility, then one plus the
				 * eligible rows that beat it outright — RANK()'s definition, restated as a count so
				 * it needs no window over the whole board. The inner FROM re-declares the same
				 * aliases; the correlated reference is to me.score only. An ineligible or absent
				 * character yields no row, which is "not ranked". */
				int characterIndex = source.Parameters.Count;
				string standingSql = $@"SELECT me.score, me.wins, me.losses,
						(SELECT COUNT(*) {source.From} WHERE {source.Where} AND {source.Score} > me.score) + 1 AS rank
					FROM (
						SELECT {source.Score} AS score, {source.Wins} AS wins, {source.Losses} AS losses
						{source.From}
						WHERE {source.Where} AND s.character_id = {{{characterIndex}}}
					) me";

				var parameters = new List<object>(source.Parameters) { characterId };
				List<LeaderboardStandingData> rows = await ReadRowsAsync(dbContext, standingSql, parameters.ToArray(), reader => new LeaderboardStandingData(
					true,
					ClampToInt(reader.GetInt64(3)),
					reader.GetInt64(0),
					reader.GetInt32(1),
					reader.GetInt32(2),
					fetchedAt), cancellationToken).ConfigureAwait(false);

				return rows.Count > 0 ? rows[0] : LeaderboardStandingData.Unranked(fetchedAt);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <summary>The SQL fragments and parameters that define one board.</summary>
		private sealed class SourceSql
		{
			/// <summary>FROM clause, aliasing the source table <c>s</c>, characters <c>c</c> and accounts <c>a</c>.</summary>
			public string From = string.Empty;
			/// <summary>WHERE predicate without the keyword: the source filter plus eligibility.</summary>
			public string Where = string.Empty;
			/// <summary>The ranked expression, as bigint.</summary>
			public string Score = string.Empty;
			public string Wins = string.Empty;
			public string Losses = string.Empty;
			/// <summary>ORDER BY terms that follow the score, to fix the order within a tie.</summary>
			public string TieBreak = string.Empty;
			/// <summary>Values for the placeholders in <see cref="Where"/>, in index order.</summary>
			public List<object> Parameters = new List<object>();
		}

		/// <summary>
		/// The one definition of a board: what is ranked, and who may appear.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Eligibility, for every source: the character row is not soft-deleted, and its account is
		/// not banned (access level 0 is how a ban is stored). Staff accounts are excluded unless
		/// the query ranks them. Joined on <c>accounts.name</c>, the principal key of the
		/// character's account foreign key.
		/// </para>
		/// <para>
		/// A score of zero is not a placing. Attribute and achievement rows exist for characters
		/// who have never earned anything (a PvP Rank of 0, a kill count of 0); ranking them would
		/// fill the bottom of every board with everyone who has ever logged in. The arena's
		/// equivalent is the minimum-games floor, never below one, so a row that was inserted and
		/// never scored cannot appear.
		/// </para>
		/// </remarks>
		private static SourceSql BuildSource(NpgsqlDbContext dbContext, LeaderboardQuery query, long seasonId)
		{
			string characters = dbContext.GetTableName<CharacterEntity>();
			string accounts = dbContext.GetTableName<AccountEntity>();

			var parameters = new List<object>(4);
			string P(object value)
			{
				parameters.Add(value);
				return "{" + (parameters.Count - 1) + "}";
			}

			string sourceTable;
			string sourceFilter;
			string score;
			string wins = "0";
			string losses = "0";
			string tieBreak;

			switch (query.Source)
			{
				case LeaderboardSourceKind.ArenaSeasonRating:
					sourceTable = dbContext.GetTableName<ArenaRatingEntity>();
					sourceFilter = $"s.season_id = {P(seasonId)} AND s.games >= {P(Math.Max(1, query.MinimumGames))}";
					score = "s.rating::bigint";
					wins = "s.wins";
					losses = "s.losses";
					// Kept from the board this replaces: at equal rating, the rating earned in fewer games leads.
					tieBreak = "s.games ASC, s.character_id ASC";
					break;
				case LeaderboardSourceKind.CharacterAttribute:
					sourceTable = dbContext.GetTableName<CharacterAttributeEntity>();
					sourceFilter = $"s.template_id = {P(query.TemplateID)} AND s.deleted = FALSE AND s.value > 0";
					score = "s.value::bigint";
					tieBreak = "s.character_id ASC";
					break;
				case LeaderboardSourceKind.CharacterAchievement:
					sourceTable = dbContext.GetTableName<CharacterAchievementEntity>();
					sourceFilter = $"s.template_id = {P(query.TemplateID)} AND s.deleted = FALSE AND s.value > 0";
					score = "s.value";
					tieBreak = "s.character_id ASC";
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(query), query.Source, "Unknown leaderboard source.");
			}

			int minimumAccess = (int)AccessLevel.Player;
			int maximumAccess = query.RankStaff ? byte.MaxValue : (int)AccessLevel.Player;
			string eligibility = $"c.deleted = FALSE AND a.access_level >= {P(minimumAccess)} AND a.access_level <= {P(maximumAccess)}";

			return new SourceSql
			{
				From = $"FROM {sourceTable} s JOIN {characters} c ON c.id = s.character_id JOIN {accounts} a ON a.name = c.account",
				Where = $"{sourceFilter} AND {eligibility}",
				Score = score,
				Wins = wins,
				Losses = losses,
				TieBreak = tieBreak,
				Parameters = parameters,
			};
		}

		/// <summary>
		/// The active arena season, read without creating one. A board read must not write, and a
		/// shard with no season yet simply has an empty arena board.
		/// </summary>
		private static async Task<(long id, string name)> ReadActiveSeasonAsync(NpgsqlDbContext dbContext, CancellationToken cancellationToken)
		{
			string seasons = dbContext.GetTableName<ArenaSeasonEntity>();
			// The partial unique index on active allows at most one; the ORDER BY only makes that explicit.
			var rows = await ReadRowsAsync(dbContext,
				$"SELECT id, name FROM {seasons} WHERE active = TRUE ORDER BY id DESC LIMIT 1",
				Array.Empty<object>(),
				reader => (reader.GetInt64(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1)),
				cancellationToken).ConfigureAwait(false);
			return rows.Count > 0 ? rows[0] : (0L, string.Empty);
		}

		private static bool IsValid(LeaderboardQuery query, out string refusal)
		{
			switch (query.Source)
			{
				case LeaderboardSourceKind.ArenaSeasonRating:
					refusal = string.Empty;
					return true;
				case LeaderboardSourceKind.CharacterAttribute:
				case LeaderboardSourceKind.CharacterAchievement:
					if (query.TemplateID == 0)
					{
						refusal = "An attribute or achievement board needs a template ID.";
						return false;
					}
					refusal = string.Empty;
					return true;
				default:
					refusal = $"Unknown leaderboard source {query.Source}.";
					return false;
			}
		}

		private static int ClampToInt(long value) => value > int.MaxValue ? int.MaxValue : (value < int.MinValue ? int.MinValue : (int)value);
	}
}
