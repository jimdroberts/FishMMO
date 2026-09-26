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
	/// <inheritdoc/>
	public sealed class SceneServerService : BaseService<SceneServerEntity>, ISceneServerService
	{
		/// <summary>
		/// Initializes a new instance of SceneServerService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public SceneServerService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <summary>
		/// A scene server row's pulse age in seconds by the database clock, as <c>double precision</c>
		/// and never negative. Expects the table to be reachable as <c>ss</c>.
		/// </summary>
		/// <remarks>
		/// <c>last_pulse</c> is stamped by the database clock (see <see cref="PulseAsync"/>), so its age
		/// is taken against the same clock, inside the statement that reads it. A reader that
		/// subtracts it from its own <c>DateTime.UtcNow</c> is measuring its host's drift from the
		/// database as much as the pulse, and liveness decisions made that way moved with every
		/// host clock. See <see cref="SceneServerData.PulseAgeSeconds"/>.
		/// </remarks>
		private const string PulseAgeSecondsSql =
			"GREATEST(0.0, EXTRACT(EPOCH FROM ((now() AT TIME ZONE 'UTC') - ss.last_pulse))::double precision)";

		/// <summary>
		/// The columns <see cref="ReadServerRow"/> maps, in its order, qualified by the alias <c>ss</c>.
		/// </summary>
		private const string ServerRowColumnsSql =
			"ss.id, ss.name, ss.last_pulse, ss.address, ss.port, ss.character_count, ss.locked, " + PulseAgeSecondsSql;

		/// <summary>
		/// Maps one row laid out as <see cref="ServerRowColumnsSql"/>, starting at ordinal 0.
		/// </summary>
		private static SceneServerData ReadServerRow(System.Data.Common.DbDataReader reader)
		{
			return new SceneServerData(
				id: reader.GetInt64(0),
				name: reader.GetString(1),
				lastPulse: DateTime.SpecifyKind(reader.GetDateTime(2), DateTimeKind.Utc),
				address: reader.GetString(3),
				port: reader.GetInt32(4),
				characterCount: reader.GetInt32(5),
				locked: reader.GetBoolean(6),
				pulseAgeSeconds: reader.IsDBNull(7) ? 0.0 : reader.GetDouble(7));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<(long ServerId, SceneServerData ServerData)>> PersistAsync(
			string name,
			string address,
			ushort port,
			int characterCount,
			bool locked,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
			{
				return DatabaseResult<(long ServerId, SceneServerData ServerData)>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Name and address must not be empty.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				/* locked and shutdown_at_utc survive registration. They are operator state and
				 * this runs on every startup, so overwriting them meant a restart silently
				 * undid the lock an operator set to make that restart safe. See the matching
				 * note in WorldServerService.PersistAsync. */
				var sql = $@"INSERT INTO {TableName} AS ss (name, address, port, character_count, locked)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}}, {{4}})
					ON CONFLICT (name)
					DO UPDATE SET
						address = EXCLUDED.address,
						port = EXCLUDED.port,
						character_count = EXCLUDED.character_count,
						last_pulse = timezone('UTC', CURRENT_TIMESTAMP)
					RETURNING {ServerRowColumnsSql}";

				return await ExecuteReturningAsync(
					dbContext,
					sql,
					new object[] { name, address, (int)port, characterCount, locked },
					ReadServerRow,
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (!result.IsSuccess)
			{
				return DatabaseResult<(long ServerId, SceneServerData ServerData)>.Failure(
					result.ErrorCode,
					result.ErrorMessage,
					result.IsTransient);
			}

			if (result.Data.ID <= 0)
			{
				return DatabaseResult<(long ServerId, SceneServerData ServerData)>.Failure(
					DatabaseErrorCodes.DatabaseError,
					"Failed to upsert scene server.",
					isTransient: true);
			}

			return DatabaseResult<(long ServerId, SceneServerData ServerData)>.Success(
				(result.Data.ID, result.Data));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<ServerControlState>> PulseAsync(long serverId, int characterCount, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult<ServerControlState>.Failure(DatabaseErrorCodes.ValidationError, "Server ID must be greater than zero.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				/* The heartbeat reads the control state back rather than writing it.
				 *
				 * It used to write `locked` from the caller's in-memory flag, which made the
				 * column unusable as a control: anything that set it — an operator, another
				 * tool — was overwritten on the next pulse, at most five seconds later. Nothing
				 * in the process ever set that flag either, so the whole feature was inert in
				 * both directions. */
				var sql = $@"UPDATE {TableName}
					SET last_pulse = timezone('UTC', CURRENT_TIMESTAMP),
						character_count = {{0}}
					WHERE id = {{1}}
					RETURNING {ServerControlSql.Columns(null)}";

				/* Nullable, so "no such row" is distinguishable from "row says unlocked".
				 *
				 * ExecuteReturningOrDefaultAsync yields default(T) when the UPDATE matched
				 * nothing, and default(ServerControlState) is a perfectly plausible healthy
				 * state — unlocked, no shutdown. A deregistered or deleted server would have
				 * read its own disappearance as an all-clear and carried on serving. */
				var state = await ExecuteReturningOrDefaultAsync(
					dbContext,
					sql,
					new object[] { characterCount, serverId },
					reader => (ServerControlState?)ServerControlSql.Read(reader, 0),
					cancellationToken).ConfigureAwait(false);

				if (!state.HasValue)
				{
					throw new DatabaseEntityNotFoundException("SceneServer", serverId.ToString());
				}

				return state.Value;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SetLockedAsync(long serverId, bool locked, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Server ID must be greater than zero.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName} SET locked = {{0}} WHERE id = {{1}}";
				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { locked, serverId },
					cancellationToken).ConfigureAwait(false);

				if (rowsAffected <= 0)
				{
					throw new DatabaseEntityNotFoundException("SceneServer", serverId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SetShutdownAsync(long serverId, DateTime? shutdownAtUtc, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Server ID must be greater than zero.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				// Scheduling locks in the same statement; cancelling leaves the lock alone.
				// Same rule as WorldServerService.SetShutdownAsync.
				/* Each branch supplies exactly the parameters its statement references.
				 * Passing a placeholder the SQL never uses leaves an unbound parameter on the
				 * command, which is at best pointless and at worst provider-specific. */
				string sql;
				object[] parameters;
				if (shutdownAtUtc.HasValue)
				{
					sql = $@"UPDATE {TableName} SET shutdown_at_utc = {{0}}, locked = true WHERE id = {{1}}";
					parameters = new object[] { shutdownAtUtc.Value, serverId };
				}
				else
				{
					sql = $@"UPDATE {TableName} SET shutdown_at_utc = NULL WHERE id = {{0}}";
					parameters = new object[] { serverId };
				}

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					parameters,
					cancellationToken).ConfigureAwait(false);

				if (rowsAffected <= 0)
				{
					throw new DatabaseEntityNotFoundException("SceneServer", serverId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<DateTime>> SetShutdownInAsync(long serverId, int seconds, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult<DateTime>.Failure(DatabaseErrorCodes.ValidationError, "Server ID must be greater than zero.");
			}
			if (seconds < 0)
			{
				// A deadline in the past is an immediate shutdown with no warning. See the callers.
				return DatabaseResult<DateTime>.Failure(DatabaseErrorCodes.ValidationError, "The delay cannot be negative.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				// The same statement as WorldServerService.SetShutdownInAsync, locking included.
				DateTime? deadline = await ExecuteReturningOrDefaultAsync(
					dbContext,
					ServerControlSql.ScheduleIn(TableName),
					new object[] { (double)seconds, serverId },
					ServerControlSql.ReadScheduled,
					cancellationToken).ConfigureAwait(false);

				if (!deadline.HasValue)
				{
					throw new DatabaseEntityNotFoundException("SceneServer", serverId.ToString());
				}

				return deadline.Value;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Server ID must be greater than zero.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE id = {{0}}";
				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { serverId }, cancellationToken)
					.ConfigureAwait(false);
				if (rowsAffected <= 0)
				{
					throw new DatabaseEntityNotFoundException("SceneServer", serverId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<SceneServerData>> FetchAsync(long serverId, CancellationToken cancellationToken = default)
		{
			if (serverId <= 0)
			{
				return DatabaseResult<SceneServerData>.Failure(DatabaseErrorCodes.ValidationError, "Server ID must be greater than zero.");
			}

			var result = await ExecuteReadAsync(async dbContext =>
			{
				// Raw SQL so the pulse age is taken by the database clock. See PulseAgeSecondsSql.
				var sql = $@"SELECT {ServerRowColumnsSql}
					FROM {TableName} AS ss
					WHERE ss.id = {{0}}";

				var rows = await ReadRowsAsync(
					dbContext,
					sql,
					new object[] { serverId },
					ReadServerRow,
					cancellationToken).ConfigureAwait(false);
				if (rows.Count == 0)
				{
					throw new DatabaseEntityNotFoundException("SceneServer", serverId.ToString());
				}
				return rows[0];
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<SceneServerData>>> FetchSceneServersByIDsAsync(
			List<long> serverIds,
			int maxBatchSize = 500,
			CancellationToken cancellationToken = default)
		{
			if (serverIds == null || serverIds.Count == 0)
			{
				return DatabaseResult<IReadOnlyList<SceneServerData>>.Success(Array.Empty<SceneServerData>());
			}

			if (maxBatchSize < 500) maxBatchSize = 500;
			else if (maxBatchSize > 1000) maxBatchSize = 1000;

			var allResults = new List<SceneServerData>(serverIds.Count);

			for (int offset = 0; offset < serverIds.Count; offset += maxBatchSize)
			{
				var batchCount = Math.Min(maxBatchSize, serverIds.Count - offset);
				var batch = serverIds.GetRange(offset, batchCount);

				var result = await ExecuteReadAsync(async dbContext =>
				{
					// Raw SQL so each pulse age is taken by the database clock. See PulseAgeSecondsSql.
					var sql = $@"SELECT {ServerRowColumnsSql}
						FROM {TableName} AS ss
						WHERE ss.id = ANY({{0}}::bigint[])";

					return await ReadRowsAsync(
						dbContext,
						sql,
						new object[] { batch.ToArray() },
						ReadServerRow,
						cancellationToken).ConfigureAwait(false);
				}, cancellationToken: cancellationToken).ConfigureAwait(false);

				if (!result.IsSuccess)
				{
					return DatabaseResult<IReadOnlyList<SceneServerData>>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
				}

				allResults.AddRange(result.Data);
			}

			return DatabaseResult<IReadOnlyList<SceneServerData>>.Success(allResults);
		}
	}
}