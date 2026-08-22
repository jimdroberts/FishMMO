using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc/>
	/// <remarks>
	/// <para><b>Error Handling:</b> All exceptions are classified by <c>BaseService</c> and mapped to
	/// <see cref="DatabaseResult"/> error codes. Transient failures are retried automatically.</para>
	/// </remarks>
	public sealed class GuildApplicationService : BaseService<GuildApplicationEntity>, IGuildApplicationService
	{
		/// <summary>
		/// Initializes a new instance of GuildApplicationService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		public GuildApplicationService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> ApplyAsync(long guildId, long characterId, string message, int maxCapacity, int maxPendingPerCharacter, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0 || characterId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid guild ID or character ID.");
			}

			if (maxCapacity <= 0 || maxPendingPerCharacter <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Invalid limits.");
			}

			string body = message ?? string.Empty;
			if (body.Length > 300)
			{
				body = body.Substring(0, 300);
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				/* Every refusal reason is a CTE evaluated in the same statement as the INSERT.
				 * The applicant chooses when to send, so any gap between the checks and the write
				 * is a gap they can aim two clients at. Ordered so the most specific reason wins
				 * the CASE — a player who is already in a guild is told that, not "guild full". */
				var sql = $@"
					WITH recruiting_guild AS (
						SELECT id FROM guilds WHERE id = {{0}} AND is_recruiting = TRUE
					),
					applicant AS (
						SELECT id FROM characters WHERE id = {{1}} AND deleted = FALSE
					),
					already_member AS (
						SELECT 1 FROM character_guild WHERE character_id = {{1}}
					),
					capacity_ok AS (
						SELECT 1 WHERE (SELECT COUNT(*) FROM character_guild WHERE guild_id = {{0}}) < {{2}}
					),
					quota_ok AS (
						SELECT 1 WHERE (SELECT COUNT(*) FROM {TableName} WHERE character_id = {{1}}) < {{3}}
					),
					inserted AS (
						INSERT INTO {TableName} (guild_id, character_id, message, time_created)
						SELECT {{0}}, {{1}}, {{4}}, {{5}}
						WHERE EXISTS (SELECT 1 FROM recruiting_guild)
						  AND EXISTS (SELECT 1 FROM applicant)
						  AND NOT EXISTS (SELECT 1 FROM already_member)
						  AND EXISTS (SELECT 1 FROM capacity_ok)
						  AND EXISTS (SELECT 1 FROM quota_ok)
						ON CONFLICT (guild_id, character_id) DO NOTHING
						RETURNING 1
					)
					SELECT CASE
						WHEN NOT EXISTS (SELECT 1 FROM applicant) THEN 1
						WHEN NOT EXISTS (SELECT 1 FROM recruiting_guild) THEN 2
						WHEN EXISTS (SELECT 1 FROM already_member) THEN 3
						WHEN NOT EXISTS (SELECT 1 FROM capacity_ok) THEN 4
						WHEN NOT EXISTS (SELECT 1 FROM quota_ok) THEN 5
						WHEN EXISTS (SELECT 1 FROM inserted) THEN 0
						ELSE 6
					END AS value";

				return await ExecuteScalarIntAsync(
					dbContext,
					sql,
					new object[] { guildId, characterId, maxCapacity, maxPendingPerCharacter, body, DateTime.UtcNow },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? result.Data switch
				{
					0 => DatabaseResult.Success(),
					1 => DatabaseResult.Failure(DatabaseErrorCodes.NotFound, $"Character with ID {characterId} was not found or has been deleted."),
					2 => DatabaseResult.Failure(DatabaseErrorCodes.NotFound, "Guild does not exist or is not recruiting."),
					3 => DatabaseResult.Failure(DatabaseErrorCodes.AlreadyExists, "Character is already in a guild."),
					4 => DatabaseResult.Failure(DatabaseErrorCodes.CapacityExceeded, "Guild is full."),
					5 => DatabaseResult.Failure(DatabaseErrorCodes.CapacityExceeded, "Too many outstanding applications."),
					_ => DatabaseResult.Failure(DatabaseErrorCodes.AlreadyExists, "An application to this guild is already pending."),
				}
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<GuildApplicationData>>> FetchManyAsync(long guildId, int limit, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
			{
				return DatabaseResult<IReadOnlyList<GuildApplicationData>>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid guild ID.");
			}

			int take = Math.Clamp(limit, 1, 100);

			return await ExecuteReadAsync(async dbContext =>
			{
				var rows = await dbContext.GuildApplications
					.AsNoTracking()
					.Where(a => a.GuildID == guildId)
					.OrderBy(a => a.TimeCreated)
					.ThenBy(a => a.ID)
					.Take(take)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				List<GuildApplicationData> entries = rows.Select(a => new GuildApplicationData(
					a.ID,
					a.GuildID,
					a.CharacterID,
					a.Message ?? string.Empty,
					a.TimeCreated)).ToList();

				return (IReadOnlyList<GuildApplicationData>)entries;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<GuildApplicationData?>> FetchAsync(long applicationId, CancellationToken cancellationToken = default)
		{
			if (applicationId <= 0)
			{
				return DatabaseResult<GuildApplicationData?>.Failure(
					DatabaseErrorCodes.ValidationError,
					"Invalid application ID.");
			}

			return await ExecuteReadAsync<GuildApplicationData?>(async dbContext =>
			{
				var row = await dbContext.GuildApplications
					.AsNoTracking()
					.FirstOrDefaultAsync(a => a.ID == applicationId, cancellationToken)
					.ConfigureAwait(false);

				if (row == null)
				{
					return null;
				}

				return new GuildApplicationData(row.ID, row.GuildID, row.CharacterID, row.Message ?? string.Empty, row.TimeCreated);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> DeleteAsync(long applicationId, long guildId, CancellationToken cancellationToken = default)
		{
			if (applicationId <= 0 || guildId <= 0)
			{
				return DatabaseResult<bool>.Failure(DatabaseErrorCodes.ValidationError, "Invalid application ID or guild ID.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE id = {{0}} AND guild_id = {{1}}";

				return await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { applicationId, guildId }, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<bool>.Success(result.Data > 0)
				: DatabaseResult<bool>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteManyByCharacterAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Invalid character ID.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE character_id = {{0}}";

				return await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { characterId }, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<GuildDirectoryEntryData>>> SearchDirectoryAsync(string searchTerm, int limit, CancellationToken cancellationToken = default)
		{
			int take = Math.Clamp(limit, 1, 50);
			string term = (searchTerm ?? string.Empty).Trim().ToLowerInvariant();

			return await ExecuteReadAsync(async dbContext =>
			{
				IQueryable<GuildEntity> query = dbContext.Guilds
					.AsNoTracking()
					.Where(g => g.IsRecruiting);

				if (term.Length > 0)
				{
					/* Matched against the lower-cased computed name column and the two
					 * already-lower-cased advertisement columns, so no per-row LOWER() is needed
					 * and the name predicate can still use the existing unique index prefix. */
					query = query.Where(g =>
						g.NameLowercase.Contains(term) ||
						g.Blurb.ToLower().Contains(term) ||
						g.Tags.Contains(term));
				}

				var rows = await query
					.OrderBy(g => g.NameLowercase)
					.Take(take)
					.Select(g => new
					{
						g.ID,
						g.Name,
						g.Blurb,
						g.Tags,
						// Counted in the same query. A count per guild afterwards would be one
						// round trip per listed guild for a page nobody has clicked on yet.
						MemberCount = g.Characters.Count(),
					})
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				List<GuildDirectoryEntryData> entries = rows.Select(g => new GuildDirectoryEntryData(
					g.ID,
					g.Name ?? string.Empty,
					g.Blurb ?? string.Empty,
					g.Tags ?? string.Empty,
					g.MemberCount)).ToList();

				return (IReadOnlyList<GuildDirectoryEntryData>)entries;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}
