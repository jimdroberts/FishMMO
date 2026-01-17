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
	public sealed class GuildService : IGuildService
	{
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Compiled query for ExistsAsync hot path.
		/// Pre-compiles the query expression tree for better performance on repeated executions.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, string, CancellationToken, Task<bool>> GuildExistsByNameQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, string upperName, CancellationToken ct) =>
				context.Guilds
					.AsNoTracking()
					.Any(g => g.Name.ToUpper() == upperName));

		/// <summary>
		/// Initializes a new instance of GuildService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public GuildService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> ExistsAsync(string name, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(name))
				return DatabaseResult<bool>.Failure("VALIDATION_ERROR", "Invalid guild name");

			try
			{
				await using var context = dbContextFactory.CreateDbContext();

				var upperName = name.ToUpper();
				// Use compiled query for hot path performance
				var exists = await GuildExistsByNameQuery(context, upperName, cancellationToken);

				return DatabaseResult<bool>.Success(exists);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<bool>.FromException(
					new DatabaseTimeoutException("GuildExists", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult<bool>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"guilds_constraint",
						"Constraint violation while checking guild existence.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<bool>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"guilds_constraint",
						"Foreign key constraint issue while checking guild existence.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<bool>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<bool>.FromException(
					new DatabaseQueryException(
						"GuildExists",
						"Failed to check guild existence due to a database error.",
						$"DbUpdateException in ExistsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<bool>.FromException(
					new DatabaseQueryException(
						"GuildExists",
						"An unexpected error occurred while checking guild existence.",
						$"Unexpected error in ExistsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<string>> GetNameByIdAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
				return DatabaseResult<string>.Failure("VALIDATION_ERROR", "Invalid guild ID");

			try
			{
				await using var context = dbContextFactory.CreateDbContext();

				var guild = await context.Guilds
					.AsNoTracking()
					.Where(g => g.ID == guildId)
					.Select(g => g.Name)
					.FirstOrDefaultAsync(cancellationToken);

				return DatabaseResult<string>.Success(guild ?? string.Empty);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<string>.FromException(
					new DatabaseTimeoutException("GetGuildName", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult<string>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"guilds_constraint",
						"Constraint violation while retrieving guild name.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<string>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"guilds_constraint",
						"Foreign key constraint issue while retrieving guild name.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<string>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<string>.FromException(
					new DatabaseQueryException(
						"GetGuildName",
						"Failed to retrieve guild name due to a database error.",
						$"DbUpdateException in GetNameByIdAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<string>.FromException(
					new DatabaseQueryException(
						"GetGuildName",
						"An unexpected error occurred while retrieving guild name.",
						$"Unexpected error in GetNameByIdAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long?>> CreateAsync(string name, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return DatabaseResult<long?>.Success(null);
			}

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = context.Database.CreateExecutionStrategy();

				var guildId = await strategy.ExecuteAsync(async () =>
				{
					var guild = new GuildEntity
					{
						Name = name,
						Notice = string.Empty
					};

					context.Guilds.Add(guild);
					await context.SaveChangesAsync(cancellationToken);
					return guild.ID;
				});

				return DatabaseResult<long?>.Success(guildId);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<long?>.FromException(
					new DatabaseTimeoutException("CreateGuild", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult<long?>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"guilds_name_key",
						"Guild name already exists.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<long?>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"guilds_constraint",
						"Foreign key constraint violation.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<long?>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<long?>.FromException(
					new DatabaseQueryException(
						"CreateGuild",
						"Failed to create guild due to a database error.",
						$"DbUpdateException in CreateAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<long?>.FromException(
					new DatabaseQueryException(
						"CreateGuild",
						"An unexpected error occurred while creating guild.",
						$"Unexpected error in CreateAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid guild ID");
			}

			await using var context = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = context.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = context.GetTableName<GuildEntity>();
					return await context.Database.ExecuteSqlInterpolatedAsync(
						$"DELETE FROM {tableName} WHERE id = {guildId}",
						cancellationToken);
				});

				// Idempotent operation - success even if not found
				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("DeleteGuild", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"guilds_constraint",
						"Constraint violation while deleting guild.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"guilds_constraint",
						"Cannot delete guild due to foreign key constraint.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteGuild",
						"Failed to delete guild due to a database error.",
						$"DbUpdateException in DeleteAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteGuild",
						"An unexpected error occurred while deleting guild.",
						$"Unexpected error in DeleteAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<GuildData?>> LoadByNameAsync(string name, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(name))
				return DatabaseResult<GuildData?>.Failure("VALIDATION_ERROR", "Invalid guild name");

			try
			{
				await using var context = dbContextFactory.CreateDbContext();

				var upperName = name.ToUpper();
				var guild = await context.Guilds
					.AsNoTracking()
					.FirstOrDefaultAsync(g => g.Name.ToUpper() == upperName, cancellationToken);

				return DatabaseResult<GuildData?>.Success(guild != null ? MapEntityToDto(guild) : null);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<GuildData?>.FromException(
					new DatabaseTimeoutException("LoadGuildByName", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult<GuildData?>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"guilds_constraint",
						"Constraint violation while loading guild.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<GuildData?>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"guilds_constraint",
						"Foreign key constraint issue while loading guild.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<GuildData?>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<GuildData?>.FromException(
					new DatabaseQueryException(
						"LoadGuildByName",
						"Failed to load guild due to a database error.",
						$"DbUpdateException in LoadByNameAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<GuildData?>.FromException(
					new DatabaseQueryException(
						"LoadGuildByName",
						"An unexpected error occurred while loading guild.",
						$"Unexpected error in LoadByNameAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<GuildData?>> LoadByIdAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId <= 0)
				return DatabaseResult<GuildData?>.Failure("VALIDATION_ERROR", "Invalid guild ID");

			try
			{
				await using var context = dbContextFactory.CreateDbContext();

				var guild = await context.Guilds
					.AsNoTracking()
					.FirstOrDefaultAsync(g => g.ID == guildId, cancellationToken);

				return DatabaseResult<GuildData?>.Success(guild != null ? MapEntityToDto(guild) : null);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<GuildData?>.FromException(
					new DatabaseTimeoutException("LoadGuildById", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult<GuildData?>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"guilds_constraint",
						"Constraint violation while loading guild.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<GuildData?>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"guilds_constraint",
						"Foreign key constraint issue while loading guild.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<GuildData?>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<GuildData?>.FromException(
					new DatabaseQueryException(
						"LoadGuildById",
						"Failed to load guild due to a database error.",
						$"DbUpdateException in LoadByIdAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<GuildData?>.FromException(
					new DatabaseQueryException(
						"LoadGuildById",
						"An unexpected error occurred while loading guild.",
						$"Unexpected error in LoadByIdAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <summary>
		/// Maps GuildEntity to GuildData DTO.
		/// </summary>
		/// <param name="entity">Guild entity from database.</param>
		/// <returns>Guild data DTO.</returns>
		private GuildData MapEntityToDto(GuildEntity entity)
		{
			return new GuildData
			{
				ID = entity.ID,
				Name = entity.Name,
				Notice = entity.Notice
			};
		}
	}
}