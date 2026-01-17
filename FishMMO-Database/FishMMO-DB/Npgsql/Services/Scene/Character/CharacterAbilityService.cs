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

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc/>
	public sealed class CharacterAbilityService : ICharacterAbilityService
	{
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterAbilityService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterAbilityService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> GetCountAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<int>.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.");
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var count = await dbContext.CharacterAbilities
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId)
					.CountAsync(cancellationToken);

				return DatabaseResult<int>.Success(count);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseTimeoutException("GetCharacterAbilityCount", 30));
			}
			catch (PostgresException pgEx)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseQueryException(
						"GetCharacterAbilityCount",
						"A database error occurred.",
						$"Database query error (SQL State: {pgEx.SqlState}): {pgEx.Message}",
						false,
						pgEx.SqlState,
						pgEx));
			}
			catch (NpgsqlException npgsqlEx)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseConnectionException("Failed to connect to the database.", npgsqlEx));
			}
			catch (Exception ex)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseException("An unexpected error occurred.", ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> SaveAbilityAsync(CharacterAbilityData abilityData, CancellationToken cancellationToken = default)
		{
			if (abilityData.CharacterID <= 0)
			{
				return DatabaseResult<long>.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.");
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();
				var strategy = dbContext.Database.CreateExecutionStrategy();

				var abilityId = await strategy.ExecuteAsync(async () =>
				{
					// Use atomic UPSERT for thread safety
					if (abilityData.ID > 0)
					{
						// Update existing ability atomically
						var tableName = dbContext.GetTableName<CharacterAbilityEntity>();
						await dbContext.Database.ExecuteSqlInterpolatedAsync(
							$@"UPDATE {tableName} 
							SET template_id = {abilityData.TemplateID},
								ability_events = {abilityData.AbilityEvents ?? new List<int>()},
								cooldown = {abilityData.Cooldown}
							WHERE id = {abilityData.ID} AND character_id = {abilityData.CharacterID}",
							cancellationToken);
						return abilityData.ID;
					}
					else
					{
						// Insert new ability
						var newAbility = new CharacterAbilityEntity
						{
							CharacterID = abilityData.CharacterID,
							TemplateID = abilityData.TemplateID,
							AbilityEvents = abilityData.AbilityEvents ?? new List<int>(),
							Cooldown = abilityData.Cooldown
						};
						dbContext.CharacterAbilities.Add(newAbility);
						await dbContext.SaveChangesAsync(cancellationToken);
						return newAbility.ID;
					}
				});

				return DatabaseResult<long>.Success(abilityId);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<long>.FromException(
					new DatabaseTimeoutException("SaveCharacterAbility", 30));
			}
			catch (PostgresException pgEx)
			{
				return DatabaseResult<long>.FromException(
					new DatabaseQueryException(
						"SaveCharacterAbility",
						"A database error occurred.",
						$"Database query error (SQL State: {pgEx.SqlState}): {pgEx.Message}",
						false,
						pgEx.SqlState,
						pgEx));
			}
			catch (NpgsqlException npgsqlEx)
			{
				return DatabaseResult<long>.FromException(
					new DatabaseConnectionException("Failed to connect to the database.", npgsqlEx));
			}
			catch (DbUpdateException dbEx)
			{
				return DatabaseResult<long>.FromException(
					new DatabaseQueryException(
						"SaveCharacterAbility",
						"A database error occurred.",
						$"Database update failed: {dbEx.Message}",
						false,
						null,
						dbEx));
			}
			catch (Exception ex)
			{
				return DatabaseResult<long>.FromException(
					new DatabaseException("An unexpected error occurred.", ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveAbilitiesAsync(IEnumerable<CharacterAbilityData> abilities, CancellationToken ct = default)
		{
			if (abilities == null || !abilities.Any())
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Abilities collection must not be null or empty.");
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();
				var strategy = dbContext.Database.CreateExecutionStrategy();

				// Execute multiple operations atomically within a transaction
				// Transaction ensures all-or-nothing semantics for INSERT + UPDATE
				// Execution strategy retries entire transaction block on transient failures
				await strategy.ExecuteAsync(async () =>
				{
					await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

					var list = abilities.ToList();
					var newItems = list.Where(a => a.ID <= 0).ToList();
					var existingItems = list.Where(a => a.ID > 0).ToList();

					// 1. Handle New Abilities via EF Core (Safe ID generation)
					if (newItems.Any())
					{
						var entities = newItems.Select(a => new CharacterAbilityEntity
						{
							CharacterID = a.CharacterID,
							TemplateID = a.TemplateID,
							AbilityEvents = a.AbilityEvents ?? new List<int>(),
							Cooldown = a.Cooldown
						});
						await dbContext.CharacterAbilities.AddRangeAsync(entities, ct);
						await dbContext.SaveChangesAsync(ct);
					}

					// 2. Handle Existing Abilities via UNNEST (Bulk Update)
					if (existingItems.Any())
					{
						var tableName = dbContext.GetTableName<CharacterAbilityEntity>();

						var ids = existingItems.Select(a => a.ID).ToArray();
						var templates = existingItems.Select(a => a.TemplateID).ToArray();
						var cooldowns = existingItems.Select(a => a.Cooldown).ToArray();
						var eventArrays = existingItems
							.Select(a => a.AbilityEvents?.ToArray() ?? Array.Empty<int>())
							.ToArray();

						await dbContext.Database.ExecuteSqlInterpolatedAsync($@"
							UPDATE {tableName} AS target
							SET template_id = source.t_id,
								ability_events = source.evs,
								cooldown = source.cd
							FROM UNNEST(
								{ids}::bigint[], 
								{templates}::int[], 
								{eventArrays}::int[][], 
								{cooldowns}::float4[]
							) AS source(id, t_id, evs, cd)
							WHERE target.id = source.id", ct);
					}

					// Commit transaction - auto-rollback on exception
					await transaction.CommitAsync(ct);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("SaveCharacterAbilities", 30));
			}
			catch (PostgresException pgEx)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SaveCharacterAbilities",
						"A database error occurred.",
						$"Database query error (SQL State: {pgEx.SqlState}): {pgEx.Message}",
						false,
						pgEx.SqlState,
						pgEx));
			}
			catch (NpgsqlException npgsqlEx)
			{
				return DatabaseResult.FromException(
					new DatabaseConnectionException("Failed to connect to the database.", npgsqlEx));
			}
			catch (DbUpdateException dbEx)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SaveCharacterAbilities",
						"A database error occurred.",
						$"Bulk save failed: {dbEx.Message}",
						false,
						null,
						dbEx));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseException("An unexpected error occurred.", ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAbilitiesAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.");
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					// Use atomic DELETE for thread safety
					var tableName = dbContext.GetTableName<CharacterAbilityEntity>();
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"DELETE FROM {tableName} WHERE character_id = {characterId}",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("DeleteCharacterAbilities", 30));
			}
			catch (PostgresException pgEx)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteCharacterAbilities",
						"A database error occurred.",
						$"Database query error (SQL State: {pgEx.SqlState}): {pgEx.Message}",
						false,
						pgEx.SqlState,
						pgEx));
			}
			catch (NpgsqlException npgsqlEx)
			{
				return DatabaseResult.FromException(
					new DatabaseConnectionException("Failed to connect to the database.", npgsqlEx));
			}
			catch (DbUpdateException dbEx)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteCharacterAbilities",
						"A database error occurred.",
						$"Database update failed: {dbEx.Message}",
						false,
						null,
						dbEx));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseException("An unexpected error occurred.", ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAbilityAsync(long characterId, long abilityId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0 || abilityId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Character ID and ability ID must be greater than 0.");
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					// Use atomic DELETE for thread safety
					var tableName = dbContext.GetTableName<CharacterAbilityEntity>();
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"DELETE FROM {tableName} WHERE character_id = {characterId} AND id = {abilityId}",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("DeleteCharacterAbility", 30));
			}
			catch (PostgresException pgEx)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteCharacterAbility",
						"A database error occurred.",
						$"Database query error (SQL State: {pgEx.SqlState}): {pgEx.Message}",
						false,
						pgEx.SqlState,
						pgEx));
			}
			catch (NpgsqlException npgsqlEx)
			{
				return DatabaseResult.FromException(
					new DatabaseConnectionException("Failed to connect to the database.", npgsqlEx));
			}
			catch (DbUpdateException dbEx)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteCharacterAbility",
						"A database error occurred.",
						$"Database update failed: {dbEx.Message}",
						false,
						null,
						dbEx));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseException("An unexpected error occurred.", ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterAbilityData>>> GetAbilitiesAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterAbilityData>>.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.");
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var abilities = await dbContext.CharacterAbilities
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId)
					.Select(a => new CharacterAbilityData
					{
						ID = a.ID,
						CharacterID = a.CharacterID,
						TemplateID = a.TemplateID,
						AbilityEvents = a.AbilityEvents,
						Cooldown = a.Cooldown
					})
					.ToListAsync(cancellationToken);

				return DatabaseResult<IReadOnlyList<CharacterAbilityData>>.Success(abilities);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<IReadOnlyList<CharacterAbilityData>>.FromException(
					new DatabaseTimeoutException("GetCharacterAbilities", 30));
			}
			catch (PostgresException pgEx)
			{
				return DatabaseResult<IReadOnlyList<CharacterAbilityData>>.FromException(
					new DatabaseQueryException(
						"GetCharacterAbilities",
						"A database error occurred.",
						$"Database query error (SQL State: {pgEx.SqlState}): {pgEx.Message}",
						false,
						pgEx.SqlState,
						pgEx));
			}
			catch (NpgsqlException npgsqlEx)
			{
				return DatabaseResult<IReadOnlyList<CharacterAbilityData>>.FromException(
					new DatabaseConnectionException("Failed to connect to the database.", npgsqlEx));
			}
			catch (Exception ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterAbilityData>>.FromException(
					new DatabaseException("An unexpected error occurred.", ex));
			}
		}
	}
}