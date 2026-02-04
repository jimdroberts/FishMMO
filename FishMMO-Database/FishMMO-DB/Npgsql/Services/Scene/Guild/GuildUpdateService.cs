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
		/// Initializes a new instance of GuildUpdateService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public GuildUpdateService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> PersistAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
			{
				return DatabaseResult.Failure("INVALID_GUILD_ID", "Guild ID must be greater than zero.");
			}

			var now = DateTime.UtcNow;
			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"INSERT INTO {TableName} (guild_id, time_created, last_update)
					VALUES ({{0}}, {{1}}, {{1}})
					ON CONFLICT (guild_id) DO UPDATE
					SET last_update = EXCLUDED.last_update
					WHERE last_update < EXCLUDED.last_update";

				await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { guildId, now },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
			{
				return DatabaseResult<int>.Failure("INVALID_GUILD_ID", "Guild ID must be greater than zero.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE guild_id = {{0}}";
				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { guildId },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
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

			return result;
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