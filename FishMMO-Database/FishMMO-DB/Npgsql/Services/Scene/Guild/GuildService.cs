using System;
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
	public sealed class GuildService : BaseService<GuildEntity>, IGuildService
	{
		/// <summary>
		/// Compiled query for ExistsAsync hot path.
		/// Pre-compiles the query expression tree for better performance on repeated executions.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<bool>> GuildExistsByNameQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string upperName, CancellationToken ct) =>
				context.Guilds
					.AsNoTracking()
					.Any(g => g.Name.ToUpper() == upperName));

		/// <summary>	/// Compiled query for LoadByIdAsync hot path (guild data retrieval).
		/// Pre-compiles the query expression tree for better performance on repeated executions.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<GuildEntity?>> GetGuildByIdQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long guildId, CancellationToken ct) =>
				context.Guilds
					.AsNoTracking()
					.FirstOrDefault(g => g.ID == guildId));
#pragma warning restore CS8619

		/// <summary>		/// Initializes a new instance of GuildService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public GuildService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> ExistsAsync(string name, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(name))
				return DatabaseResult<bool>.Failure("VALIDATION_ERROR", "Invalid guild name");

			return await ExecuteWithStrategyAsync(async context =>
			{
				var upperName = name.ToUpper();
				return await GuildExistsByNameQuery(context, upperName, cancellationToken);
			}, "GuildExists", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<string>> GetNameByIdAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
				return DatabaseResult<string>.Failure("VALIDATION_ERROR", "Invalid guild ID");

			return await ExecuteWithStrategyAsync(async context =>
			{
				var guild = await context.Guilds
					.AsNoTracking()
					.Where(g => g.ID == guildId)
					.Select(g => g.Name)
					.FirstOrDefaultAsync(cancellationToken);

				return guild ?? string.Empty;
			}, "GetGuildName", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long?>> CreateAsync(string name, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return DatabaseResult<long?>.Success(null);
			}

			return await ExecuteWithStrategyAsync(async context =>
			{
				// Use atomic INSERT with RETURNING for proper retry strategy support
				// Optimized: RETURNING only id for better performance
				var result = await context.Guilds
					.FromSqlInterpolated($@"
					INSERT INTO {TableName} (name, notice, time_created)
					VALUES ({name}, {string.Empty}, CURRENT_TIMESTAMP)
					RETURNING id")
						.AsNoTracking()
						.FirstOrDefaultAsync(cancellationToken);

				return (long?)result?.ID;
			}, "CreateGuild", cancellationToken);
		}

		/// <inheritdoc/>
		/// <remarks>
		/// <para><b>Transaction Scope:</b></para>
		/// This operation uses an explicit transaction to ensure atomicity.
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
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid guild ID");
			}

			// Use explicit transaction for atomic multi-table operation
			return await ExecuteInTransactionAsync(async (dbContext, transaction) =>
			{
				var rowsAffected = await dbContext.Database.ExecuteSqlInterpolatedAsync(
					$"DELETE FROM {TableName} WHERE id = {guildId}",
					cancellationToken);

				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException("Guild", guildId.ToString());
				}
			}, "DeleteGuild", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<GuildData?>> LoadByNameAsync(string name, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(name))
				return DatabaseResult<GuildData?>.Failure("VALIDATION_ERROR", "Invalid guild name");

			return await ExecuteWithStrategyAsync(async context =>
			{
				var upperName = name.ToUpper();
				var guild = await context.Guilds
					.AsNoTracking().FirstOrDefaultAsync(g => g.Name.ToUpper() == upperName, cancellationToken);

				return guild != null ? MapEntityToDto(guild) : (GuildData?)null;
			}, "LoadGuildByName", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<GuildData?>> LoadByIdAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
				return DatabaseResult<GuildData?>.Failure("VALIDATION_ERROR", "Invalid guild ID");

			return await ExecuteWithStrategyAsync(async context =>
			{
				var guild = await GetGuildByIdQuery(context, guildId, cancellationToken);

				return guild != null ? MapEntityToDto(guild) : (GuildData?)null;
			}, "LoadGuildById", cancellationToken);
		}

		/// <summary>
		/// Maps GuildEntity to GuildData DTO.
		/// </summary>
		/// <param name="entity">Guild entity from database.</param>
		/// <returns>Guild data DTO.</returns>
		private GuildData MapEntityToDto(GuildEntity entity)
		{
			return new GuildData(entity.ID, entity.Name, entity.Notice);
		}
	}
}