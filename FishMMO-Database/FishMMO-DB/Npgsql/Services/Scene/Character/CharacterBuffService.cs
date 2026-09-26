using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Character buff service with async operations, atomic SQL, and DTO pattern.
	/// Uses repository pattern with EF Core and raw SQL for race-condition-prone operations.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling with sanitized messages.
	/// Follows SOLID principles: SRP, OCP, LSP, ISP, DIP.
	/// </summary>
	/// <remarks>
	/// All methods that use ExecuteSqlRawAsync are wrapped in execution strategies
	/// to provide automatic retry logic (up to 3 attempts) for transient database failures
	/// such as connection timeouts, deadlocks, or network interruptions.
	/// 
	/// Exception Handling Strategy:
	/// - Catches specific exceptions (NpgsqlException, DbUpdateException, TimeoutException)
	/// - Converts to custom DatabaseException hierarchy with sanitized messages
	/// - Returns DatabaseResult for safe, typed error handling
	/// - Preserves detailed error information for logging while exposing safe messages to clients
	///
	/// Writes happen only through <see cref="ReplaceSetsAsync"/>, which <see cref="CharacterService"/>
	/// runs inside its own character-row transaction; see <see cref="ICharacterBuffService"/>.
	/// </remarks>
	public sealed class CharacterBuffService : BaseService<CharacterBuffEntity>, ICharacterBuffService
	{
		/// <summary>
		/// Compiled query for retrieving character buffs (hot path for character state).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, IAsyncEnumerable<CharacterBuffEntity>> getBuffsQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId) =>
				context.CharacterBuffs
					.AsNoTracking()
					.Where(b => b.CharacterID == characterId && !b.Deleted));

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterBuffService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterBuffService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <summary>
		/// Every row of a batch of buff sets, flattened into the column arrays the replace statement
		/// binds.
		/// </summary>
		/// <remarks>
		/// <see cref="Owners"/> names every character whose set is being replaced, including those
		/// whose set is empty: an owner with no rows is how "this character has no buffs" reaches the
		/// database, and it is the case the old upsert could not express at all.
		/// </remarks>
		public sealed class BuffSetRows
		{
			/// <summary>Every character whose stored set is replaced. Distinct.</summary>
			public long[] Owners;
			/// <summary>Per row: the owning character.</summary>
			public long[] CharacterIds;
			/// <summary>Per row: the buff template.</summary>
			public int[] TemplateIds;
			/// <summary>Per row: the version stamped on it, which is its character's snapshot version.</summary>
			public long[] Versions;
			/// <summary>Per row: seconds of duration left.</summary>
			public double[] RemainingTimes;
			/// <summary>Per row: seconds until the next tick.</summary>
			public double[] TickTimes;
			/// <summary>Per row: stacks.</summary>
			public int[] Stacks;
			/// <summary>Per row: ticks fired so far.</summary>
			public int[] TickCounts;

			/// <summary>Number of buff rows (not owners).</summary>
			public int RowCount => CharacterIds.Length;
		}

		/// <summary>
		/// Flattens buff sets into the arrays <see cref="ReplaceSetsAsync"/> binds. Pure.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The set decides whose rows they are, and which version they carry.</b> Every row is
		/// written under its set's character and its set's version, whatever the DTO says: a set is
		/// written only after its character row has passed that row's version and ownership checks in
		/// the same transaction, so the row's character and version are the ones proven, and a row
		/// naming anyone else must not ride on that proof.
		/// </para>
		/// <para>
		/// A template named twice in one set keeps its first occurrence. The capture reads a
		/// dictionary keyed by template, so a duplicate is a caller defect — and an
		/// <c>INSERT ... ON CONFLICT</c> that meets one key twice fails the whole statement, which
		/// would take the character row down with it. A character named by two sets keeps the one
		/// with the higher version, for the reason <c>PersistManyAsync</c> keeps the newer of two
		/// snapshots.
		/// </para>
		/// </remarks>
		/// <param name="sets">The sets to write: the character, its snapshot version, and its complete set of buffs (never null; empty means none).</param>
		/// <returns>The column arrays, or null when <paramref name="sets"/> names nobody.</returns>
		public static BuffSetRows FlattenSets(IReadOnlyList<(long CharacterID, long Version, IReadOnlyList<CharacterBuffData> Buffs)> sets)
		{
			if (sets == null || sets.Count == 0)
			{
				return null;
			}

			var newest = new Dictionary<long, (long Version, IReadOnlyList<CharacterBuffData> Buffs)>(sets.Count);
			var order = new List<long>(sets.Count);
			for (int i = 0; i < sets.Count; ++i)
			{
				(long characterId, long version, IReadOnlyList<CharacterBuffData> buffs) = sets[i];
				if (characterId <= 0 || buffs == null)
				{
					continue;
				}
				if (newest.TryGetValue(characterId, out var held))
				{
					if (version > held.Version)
					{
						newest[characterId] = (version, buffs);
					}
					continue;
				}
				newest[characterId] = (version, buffs);
				order.Add(characterId);
			}

			if (order.Count == 0)
			{
				return null;
			}

			var characterIds = new List<long>();
			var templateIds = new List<int>();
			var versions = new List<long>();
			var remainingTimes = new List<double>();
			var tickTimes = new List<double>();
			var stacks = new List<int>();
			var tickCounts = new List<int>();
			var seen = new HashSet<int>();

			foreach (long characterId in order)
			{
				(long version, IReadOnlyList<CharacterBuffData> buffs) = newest[characterId];
				seen.Clear();
				for (int i = 0; i < buffs.Count; ++i)
				{
					CharacterBuffData buff = buffs[i];
					if (!seen.Add(buff.TemplateID))
					{
						continue;
					}
					characterIds.Add(characterId);
					templateIds.Add(buff.TemplateID);
					versions.Add(version);
					remainingTimes.Add(buff.RemainingTime);
					tickTimes.Add(buff.TickTime);
					stacks.Add(buff.Stacks);
					tickCounts.Add(buff.TickCount);
				}
			}

			return new BuffSetRows
			{
				Owners = order.ToArray(),
				CharacterIds = characterIds.ToArray(),
				TemplateIds = templateIds.ToArray(),
				Versions = versions.ToArray(),
				RemainingTimes = remainingTimes.ToArray(),
				TickTimes = tickTimes.ToArray(),
				Stacks = stacks.ToArray(),
				TickCounts = tickCounts.ToArray(),
			};
		}

		/// <summary>
		/// Makes each named character's stored buffs exactly its set, inside the caller's
		/// transaction. One statement for the whole batch.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Only <c>ICharacterService</c>'s row writes call this, and only for rows they have just
		/// written.</b> That is the entire concurrency argument, and it is why this method carries no
		/// version guard of its own. The character row's <c>UPDATE ... WHERE version &lt; incoming</c>
		/// (plus the ownership triple when a claim is held) runs first in the same transaction and
		/// holds the row lock until the commit, so for any one character the sets are applied in the
		/// order of their snapshots' versions: a newer save that already committed makes an older
		/// one's row update match nothing, and that older save then writes no set at all — it cannot
		/// delete a buff the newer save wrote, nor re-add one the newer save dropped. Two saves in
		/// flight together serialise on the row lock and the second re-checks the version after the
		/// first commits.
		/// </para>
		/// <para>
		/// Per-row versions could not do this. A buff that ended and was applied again is a new
		/// instance whose counter restarts, so it was refused as stale against its own dead
		/// predecessor's row for as many saves as that row had seen; and a set that became empty has
		/// no row left to carry a version, so an older save landing after it could re-insert what it
		/// had dropped. The row version is stamped from the snapshot for inspection only.
		/// </para>
		/// <para>
		/// Rows a set does not name are hard-deleted, soft-deleted ones included: this is live
		/// gameplay churn, not an audit trail, and the row guard above is what orders writers, so no
		/// tombstone is needed to refuse a late one. The deletion runs in the same statement as the
		/// upsert and the two touch disjoint keys — what the set names versus what it does not — so
		/// the shared snapshot of a data-modifying <c>WITH</c> cannot make them disagree.
		/// </para>
		/// </remarks>
		/// <param name="dbContext">The caller's context, with its transaction open.</param>
		/// <param name="rows">The flattened sets, from <see cref="FlattenSets"/>.</param>
		/// <param name="cancellationToken">Cancellation token.</param>
		internal static async Task ReplaceSetsAsync(NpgsqlDbContext dbContext, BuffSetRows rows, CancellationToken cancellationToken)
		{
			if (rows == null || rows.Owners == null || rows.Owners.Length == 0)
			{
				return;
			}

			string table = dbContext.GetTableName<CharacterBuffEntity>();
			string sql = $@"
				WITH incoming AS (
					SELECT *
					FROM UNNEST(
						{{0}}::bigint[],
						{{1}}::integer[],
						{{2}}::bigint[],
						{{3}}::double precision[],
						{{4}}::double precision[],
						{{5}}::integer[],
						{{6}}::integer[]
					) AS u(character_id, template_id, version, remaining_time, tick_time, stacks, tick_count)
				),
				dropped AS (
					DELETE FROM {table} AS b
					WHERE b.character_id = ANY({{7}}::bigint[])
						AND NOT EXISTS (
							SELECT 1 FROM incoming AS i
							WHERE i.character_id = b.character_id AND i.template_id = b.template_id)
				)
				INSERT INTO {table}
					(character_id, template_id, version, remaining_time, tick_time, stacks, tick_count, time_created, deleted, time_deleted)
				SELECT
					i.character_id, i.template_id, i.version, i.remaining_time, i.tick_time, i.stacks, i.tick_count,
					{{8}}, FALSE, NULL
				FROM incoming AS i
				ON CONFLICT (character_id, template_id)
				DO UPDATE SET
					remaining_time = EXCLUDED.remaining_time,
					tick_time = EXCLUDED.tick_time,
					stacks = EXCLUDED.stacks,
					tick_count = EXCLUDED.tick_count,
					deleted = FALSE,
					time_deleted = NULL,
					version = EXCLUDED.version";

			await dbContext.Database.ExecuteSqlRawAsync(
				sql,
				new object[]
				{
					rows.CharacterIds,
					rows.TemplateIds,
					rows.Versions,
					rows.RemainingTimes,
					rows.TickTimes,
					rows.Stacks,
					rows.TickCounts,
					rows.Owners,
					DateTime.UtcNow,
				},
				cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long characterId, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid character ID. Character ID must be greater than 0.");
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid Version. Version must be greater than 0.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sql = $@"UPDATE {TableName}
					SET deleted = TRUE, time_deleted = {{0}}, version = {{1}}
					WHERE character_id = {{2}} AND deleted = FALSE AND version < {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { now, incomingVersion, characterId }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var anyActive = await dbContext.CharacterBuffs
						.AsNoTracking()
						.AnyAsync(b => b.CharacterID == characterId && !b.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (anyActive)
					{
						throw new StaleStateException("Buff delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterBuffData>>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterBuffData>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid character ID. Character ID must be greater than 0.");
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getBuffsQuery(dbContext, characterId).MaterializeAsync(cancellationToken).ConfigureAwait(false);
				var buffs = entities.Select(b => new CharacterBuffData(
					id: b.ID,
					version: b.Version,
					characterID: b.CharacterID,
					templateID: b.TemplateID,
					remainingTime: b.RemainingTime,
					tickTime: b.TickTime,
					stacks: b.Stacks,
					tickCount: b.TickCount
				)).ToList();

				return (IReadOnlyList<CharacterBuffData>)buffs;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}