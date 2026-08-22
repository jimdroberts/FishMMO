using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Shared;

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc/>
	/// <remarks>
	/// <para><b>Error Handling:</b> All exceptions are classified by <c>BaseService</c> and mapped to <see cref="DatabaseResult"/> error codes
	/// (e.g., UNIQUE_VIOLATION, FOREIGN_KEY_VIOLATION, STALE_STATE, DATABASE_ERROR). Transient failures are retried automatically.</para>
	/// <para>Unique constraint violations are treated as failures; callers should not depend on them for normal control flow.</para>
	/// </remarks>
	public sealed class GuildService : BaseService<GuildEntity>, IGuildService
	{
		/// <summary>
		/// Compiled query for ExistsAsync hot path.
		/// Pre-compiles the query expression tree for better performance on repeated executions.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<bool>> guildExistsByNameQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string nameLowercase, CancellationToken ct) =>
				context.Guilds
					.AsNoTracking()
					.Any(g => g.NameLowercase == nameLowercase));

		/// <summary>
		/// Compiled query for FetchAsync (by guild id) hot path (guild data retrieval).
		/// Pre-compiles the query expression tree for better performance on repeated executions.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<GuildEntity?>> getGuildByIdQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long guildId, CancellationToken ct) =>
				context.Guilds
					.AsNoTracking()
					.FirstOrDefault(g => g.ID == guildId));
#pragma warning restore CS8619

		/// <summary>
		/// Initializes a new instance of GuildService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public GuildService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> ExistsAsync(string name, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedGuildName(name))
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidGuildNameError);

			var result = await ExecuteReadAsync(async context =>
			{
				var nameLowercase = name.ToLowerInvariant();
				return await guildExistsByNameQuery(context, nameLowercase, cancellationToken).ConfigureAwait(false);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<string?>> FetchNameAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
				return DatabaseResult<string?>.Failure(DatabaseErrorCodes.ValidationError, "Invalid guild ID.");

			var result = await ExecuteReadAsync(async context =>
			{
				return await context.Guilds
					.AsNoTracking()
					.Where(g => g.ID == guildId)
					.Select(g => g.Name)
					.FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long?>> PersistAsync(string name, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedGuildName(name))
				return DatabaseResult<long?>.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidGuildNameError);

			var nameLowercase = name.ToLowerInvariant();

			var result = await ExecuteWriteAsync<long?>(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sql = $@"
					WITH inserted AS (
						INSERT INTO {TableName} (name, notice, message_of_the_day, time_created)
						VALUES ({{0}}, '', '', {{1}})
						ON CONFLICT (name_lowercase)
						DO NOTHING
						RETURNING id
					)
					SELECT COALESCE((SELECT id FROM inserted), 0)::bigint AS value";

				var id = await ExecuteScalarLongAsync(
					dbContext,
					sql,
					new object[] { name, now, nameLowercase },
					cancellationToken).ConfigureAwait(false);

				/* Zero means ON CONFLICT DO NOTHING matched — the name is taken and this call
				 * inserted nothing.
				 *
				 * This used to COALESCE to the id of the guild that already owned the name, and
				 * hand it back as though it had just been created. The caller's only protection
				 * was a separate ExistsAsync round trip, which is a plain time-of-check /
				 * time-of-use window: two founders racing on the same name both passed the
				 * check, and the loser was then written into the WINNER'S guild at the default
				 * leader rank order — full leader permissions over a guild they had never seen.
				 * Reporting the conflict is what makes that unrepresentable. */
				if (id <= 0)
				{
					return null;
				}

				return (long?)id;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			// A successful call that inserted nothing is a name collision, not a success.
			if (result.IsSuccess && !result.Data.HasValue)
			{
				return DatabaseResult<long?>.Failure(DatabaseErrorCodes.AlreadyExists, "A guild with that name already exists.");
			}
			return result;
		}

		/// <inheritdoc/>
		/// <remarks>
		/// <para><b>Atomicity:</b></para>
		/// This operation uses a single DELETE statement.
		/// CASCADE delete constraints automatically remove related data:
		/// <list type="bullet">
		/// <item>All character guild memberships (character_guild table)</item>
		/// <item>Guild update notifications (guild_update table)</item>
		/// </list>
		/// </remarks>
		public async Task<DatabaseResult> DeleteAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid guild ID.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				// Rely on ON DELETE CASCADE constraints to remove related rows.
				var sql = $@"DELETE FROM {TableName} WHERE id = {{0}}";
				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { guildId }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected <= 0)
				{
					throw new DatabaseEntityNotFoundException("Guild", guildId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<GuildData?>> FetchAsync(string name, CancellationToken cancellationToken = default)
		{
			if (!Authentication.IsAllowedGuildName(name))
				return DatabaseResult<GuildData?>.Failure(DatabaseErrorCodes.ValidationError, Authentication.InvalidGuildNameError);

			var result = await ExecuteReadAsync(async context =>
			{
				var guild = await context.Guilds
					.AsNoTracking()
					.FirstOrDefaultAsync(g => g.NameLowercase == name.ToLowerInvariant(), cancellationToken)
					.ConfigureAwait(false);

				return guild != null ? MapEntityToDto(guild) : (GuildData?)null;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<GuildData?>> FetchAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
				return DatabaseResult<GuildData?>.Failure(DatabaseErrorCodes.ValidationError, "Invalid guild ID.");

			var result = await ExecuteReadAsync(async context =>
			{
				var guild = await getGuildByIdQuery(context, guildId, cancellationToken).ConfigureAwait(false);
				return guild != null ? MapEntityToDto(guild) : (GuildData?)null;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
			return result;
		}

		/// <summary>
		/// Maps GuildEntity to GuildData DTO.
		/// </summary>
		/// <param name="entity">Guild entity from database.</param>
		/// <returns>Guild data DTO.</returns>
		private GuildData MapEntityToDto(GuildEntity entity)
		{
			return new GuildData(
				entity.ID,
				entity.Name,
				entity.Notice,
				entity.MessageOfTheDay ?? string.Empty,
				entity.Blurb ?? string.Empty,
				entity.Tags ?? string.Empty,
				entity.IsRecruiting);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistRecruitmentAsync(long guildId, string blurb, string tags, bool isRecruiting, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid guild ID.");

			string body = blurb ?? string.Empty;
			if (body.Length > 500)
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Blurb must not exceed 500 characters.");

			string tagList = tags ?? string.Empty;
			if (tagList.Length > 200)
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Tags must not exceed 200 characters.");

			return await ExecuteWriteAsync(async dbContext =>
			{
				// Stored already lower-cased so directory search never needs a per-row LOWER().
				var sql = $@"UPDATE {TableName} SET blurb = {{0}}, tags = {{1}}, is_recruiting = {{2}} WHERE id = {{3}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { body, tagList.ToLowerInvariant(), isRecruiting, guildId }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Guild", guildId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistNoticeAsync(long guildId, string notice, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid guild ID.");

			if (notice != null && notice.Length > 500)
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Notice must not exceed 500 characters.");

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName} SET notice = {{0}} WHERE id = {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { notice ?? string.Empty, guildId }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Guild", guildId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistMessageOfTheDayAsync(long guildId, string messageOfTheDay, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid guild ID.");

			if (messageOfTheDay != null && messageOfTheDay.Length > 500)
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Message of the day must not exceed 500 characters.");

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"UPDATE {TableName} SET message_of_the_day = {{0}} WHERE id = {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { messageOfTheDay ?? string.Empty, guildId }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Guild", guildId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}