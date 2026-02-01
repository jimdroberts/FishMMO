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
	public sealed class GuildUpdateService : BaseService<GuildUpdateEntity>, IGuildUpdateService
	{
		/// <summary>
		/// Compiled query for retrieving a tracked guild update row by guild ID.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<GuildUpdateEntity?>> getByGuildIdTrackingQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long guildId, CancellationToken ct) =>
				context.GuildUpdates.FirstOrDefault(u => u.GuildID == guildId));
#pragma warning restore CS8619

		/// <summary>
		/// Initializes a new instance of GuildUpdateService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public GuildUpdateService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
			{
				return DatabaseResult.Failure("INVALID_GUILD_ID", "Guild ID must be greater than zero.");
			}

			var now = DateTime.UtcNow;
			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				var existing = await getByGuildIdTrackingQuery(dbContext, guildId, cancellationToken).ConfigureAwait(false);
				if (existing == null)
				{
					existing = new GuildUpdateEntity
					{
						GuildID = guildId,
						TimeCreated = now,
						LastUpdate = now,
					};
					await dbContext.GuildUpdates.AddAsync(existing, cancellationToken).ConfigureAwait(false);
					return;
				}

				if (existing.LastUpdate < now)
				{
					existing.LastUpdate = now;
				}
			}).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
			{
				return DatabaseResult<int>.Failure("INVALID_GUILD_ID", "Guild ID must be greater than zero.");
			}

			var result = await ExecuteTransactionAsync(async dbContext =>
			{
				var existing = await getByGuildIdTrackingQuery(dbContext, guildId, cancellationToken).ConfigureAwait(false);
				if (existing == null)
				{
					return 0;
				}
				dbContext.GuildUpdates.Remove(existing);
				return 1;
			}).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<int>.Success(result.Data)
				: DatabaseResult<int>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<List<GuildUpdateData>>> FetchAsync(
			List<long> guildIds,
			DateTime lastFetch,
			CancellationToken cancellationToken = default)
		{
			if (guildIds == null || guildIds.Count == 0)
				return DatabaseResult<List<GuildUpdateData>>.Success(new List<GuildUpdateData>());

			var result = await ExecuteReadAsync(async dbContext =>
			{
				var guildIdArray = guildIds.Distinct().ToArray();
				var sql = $@"SELECT * FROM {TableName}
					WHERE last_update >= {{0}}
					AND guild_id = ANY({{1}})";

				var updates = await dbContext.GuildUpdates
					.FromSqlRaw(sql, lastFetch, guildIdArray)
					.AsNoTracking()
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return updates.Select(MapEntityToDto).ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<List<GuildUpdateData>>.Success(result.Data)
				: DatabaseResult<List<GuildUpdateData>>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <summary>
		/// Maps GuildUpdateEntity to GuildUpdateData DTO.
		/// </summary>
		/// <param name="entity">Guild update entity from database.</param>
		/// <returns>Guild update data DTO.</returns>
		private GuildUpdateData MapEntityToDto(GuildUpdateEntity entity)
		{
			return new GuildUpdateData(
				id: entity.ID,
				guildID: entity.GuildID,
				lastUpdate: entity.LastUpdate);
		}
	}
}