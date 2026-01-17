using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc/>
	/// <remarks>
	/// <para><b>Exception Handling:</b></para>
	/// <list type="bullet">
	/// <item><description><see cref="OperationCanceledException"/> → <see cref="DatabaseTimeoutException"/></description></item>
	/// <item><description><see cref="PostgresException"/> (23505) → <see cref="DatabaseConstraintException"/> (Unique)</description></item>
	/// <item><description><see cref="PostgresException"/> (23503) → <see cref="DatabaseConstraintException"/> (ForeignKey)</description></item>
	/// <item><description><see cref="NpgsqlException"/> → <see cref="DatabaseConnectionException"/></description></item>
	/// <item><description><see cref="DbUpdateException"/> → <see cref="DatabaseQueryException"/></description></item>
	/// <item><description><see cref="Exception"/> → <see cref="DatabaseQueryException"/></description></item>
	/// </list>
	/// </remarks>
	public sealed class GuildUpdateService : IGuildUpdateService
	{
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of GuildUpdateService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public GuildUpdateService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
			{
				return DatabaseResult.Failure("INVALID_GUILD_ID", "Guild ID must be greater than zero.");
			}

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = context.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					// Atomic UPSERT - PostgreSQL specific
					var tableName = context.GetTableName<GuildUpdateEntity>();
					await context.Database.ExecuteSqlInterpolatedAsync(
						$@"INSERT INTO {tableName} (guild_id, last_update) 
						VALUES ({guildId}, CURRENT_TIMESTAMP) 
						ON CONFLICT (guild_id) 
						DO UPDATE SET last_update = EXCLUDED.last_update 
						WHERE {tableName}.last_update < EXCLUDED.last_update",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult.FromException(new DatabaseTimeoutException(
					"SaveGuildUpdate",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"guild_updates_pkey",
					"A guild update record with this ID already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"guild_updates_guild_id_fkey",
					"The referenced guild does not exist.",
					ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult.FromException(new DatabaseConnectionException(
					context?.Database.GetConnectionString() ?? "unknown",
					ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult.FromException(new DatabaseQueryException(
					"SaveGuildUpdate",
					"Failed to save guild update record.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(new DatabaseQueryException(
					"SaveGuildUpdate",
					"An unexpected error occurred while saving guild update.",
					ex.Message,
					false,
					null,
					ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
			{
				return DatabaseResult<int>.Failure("INVALID_GUILD_ID", "Guild ID must be greater than zero.");
			}

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = context.Database.CreateExecutionStrategy();

				var rowsDeleted = await strategy.ExecuteAsync(async () =>
				{
					var tableName = context.GetTableName<GuildUpdateEntity>();
					return await context.Database.ExecuteSqlInterpolatedAsync(
						$"DELETE FROM {tableName} WHERE guild_id = {guildId}",
						cancellationToken);
				});

				return DatabaseResult<int>.Success(rowsDeleted);
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult<int>.FromException(new DatabaseTimeoutException(
					"DeleteGuildUpdate",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult<int>.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"guild_updates_pkey",
					"A guild update record with this ID already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult<int>.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"guild_updates_guild_id_fkey",
					"The referenced guild does not exist.",
					ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<int>.FromException(new DatabaseConnectionException(
					context?.Database.GetConnectionString() ?? "unknown",
					ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<int>.FromException(new DatabaseQueryException(
					"DeleteGuildUpdate",
					"Failed to delete guild update record.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<int>.FromException(new DatabaseQueryException(
					"DeleteGuildUpdate",
					"An unexpected error occurred while deleting guild update.",
					ex.Message,
					false,
					null,
					ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<List<GuildUpdateData>>> FetchAsync(
			List<long> guildIds,
			DateTime lastFetch,
			CancellationToken cancellationToken = default)
		{
			if (guildIds == null || guildIds.Count == 0)
				return DatabaseResult<List<GuildUpdateData>>.Success(new List<GuildUpdateData>());

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var updates = await context.GuildUpdates
					.AsNoTracking()
					.Where(u => u.LastUpdate >= lastFetch && guildIds.Contains(u.GuildID))
					.ToListAsync(cancellationToken);

				return DatabaseResult<List<GuildUpdateData>>.Success(updates.Select(MapEntityToDto).ToList());
			}
			catch (OperationCanceledException ex)
			{
				return DatabaseResult<List<GuildUpdateData>>.FromException(new DatabaseTimeoutException(
					"FetchGuildUpdates",
					30,
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505")
			{
				return DatabaseResult<List<GuildUpdateData>>.FromException(new DatabaseConstraintException(
					ConstraintType.Unique,
					"guild_updates_pkey",
					"A guild update record with this ID already exists.",
					ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503")
			{
				return DatabaseResult<List<GuildUpdateData>>.FromException(new DatabaseConstraintException(
					ConstraintType.ForeignKey,
					"guild_updates_guild_id_fkey",
					"The referenced guild does not exist.",
					ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<List<GuildUpdateData>>.FromException(new DatabaseConnectionException(
					context?.Database.GetConnectionString() ?? "unknown",
					ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<List<GuildUpdateData>>.FromException(new DatabaseQueryException(
					"FetchGuildUpdates",
					"Failed to fetch guild updates.",
					ex.Message,
					false,
					null,
					ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<List<GuildUpdateData>>.FromException(new DatabaseQueryException(
					"FetchGuildUpdates",
					"An unexpected error occurred while fetching guild updates.",
					ex.Message,
					false,
					null,
					ex));
			}
		}

		/// <summary>
		/// Maps GuildUpdateEntity to GuildUpdateData DTO.
		/// </summary>
		/// <param name="entity">Guild update entity from database.</param>
		/// <returns>Guild update data DTO.</returns>
		private GuildUpdateData MapEntityToDto(GuildUpdateEntity entity)
		{
			return new GuildUpdateData
			{
				GuildID = entity.GuildID,
				LastUpdate = entity.LastUpdate
			};
		}
	}
}