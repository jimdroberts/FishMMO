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
		public async Task<DatabaseResult> SaveAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
			{
				return DatabaseResult.Failure("INVALID_GUILD_ID", "Guild ID must be greater than zero.");
			}

			var result = await ExecuteSqlAsync(
				$@"INSERT INTO {TableName} (guild_id, last_update) 
					VALUES ({guildId}, CURRENT_TIMESTAMP) 
					ON CONFLICT (guild_id) 
					DO UPDATE SET last_update = GREATEST({TableName}.last_update, EXCLUDED.last_update)",
				"SaveGuildUpdate",
				entityName: "GuildUpdate",
				entityId: guildId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
			{
				return DatabaseResult<int>.Failure("INVALID_GUILD_ID", "Guild ID must be greater than zero.");
			}

			return await ExecuteSqlAsync(
				$"DELETE FROM {TableName} WHERE guild_id = {guildId}",
				"DeleteGuildUpdate",
				entityName: "GuildUpdate",
				entityId: guildId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<List<GuildUpdateData>>> FetchAsync(
			List<long> guildIds,
			DateTime lastFetch,
			CancellationToken cancellationToken = default)
		{
			if (guildIds == null || guildIds.Count == 0)
				return DatabaseResult<List<GuildUpdateData>>.Success(new List<GuildUpdateData>());

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				var updates = await dbContext.GuildUpdates
					.AsNoTracking()
					.Where(u => u.LastUpdate >= lastFetch && guildIds.Contains(u.GuildID))
					.ToListAsync(cancellationToken);

				return updates.Select(MapEntityToDto).ToList();
			}, "FetchGuildUpdates", cancellationToken);
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