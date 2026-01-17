using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc/>
	public sealed class CharacterAttributeService : ICharacterAttributeService
	{
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterAttributeService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterAttributeService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveAttributesAsync(IEnumerable<CharacterAttributeData> attributes, CancellationToken cancellationToken = default)
		{
			if (attributes == null || !attributes.Any())
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Attributes collection must not be null or empty.");
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterAttributeEntity>();
					var attributeList = attributes.ToList();

					// Extract arrays for bulk UPSERT
					var characterIds = attributeList.Select(a => a.CharacterID).ToArray();
					var templateIds = attributeList.Select(a => a.TemplateID).ToArray();
					var values = attributeList.Select(a => a.Value).ToArray();
					var currentValues = attributeList.Select(a => a.CurrentValue).ToArray();

					// Single bulk UPSERT using UNNEST - atomic operation, no transaction needed
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"INSERT INTO {tableName} 
						(character_id, template_id, value, current_value)
						SELECT * FROM UNNEST(
							{characterIds}::bigint[],
							{templateIds}::int[],
							{values}::int[],
							{currentValues}::float4[]
						)
						ON CONFLICT (character_id, template_id) 
						DO UPDATE SET 
							value = EXCLUDED.value,
							current_value = EXCLUDED.current_value",
							cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("SaveCharacterAttributes", 30));
			}
			catch (PostgresException pgEx)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SaveCharacterAttributes",
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
						"SaveCharacterAttributes",
						"A database error occurred.",
						$"Database error: {dbEx.Message}",
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
		public async Task<DatabaseResult> DeleteAttributesAsync(long characterId, CancellationToken cancellationToken = default)
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
					var tableName = dbContext.GetTableName<CharacterAttributeEntity>();
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"DELETE FROM {tableName} WHERE character_id = {characterId}",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("DeleteCharacterAttributes", 30));
			}
			catch (PostgresException pgEx)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteCharacterAttributes",
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
						"DeleteCharacterAttributes",
						"A database error occurred.",
						$"Database error: {dbEx.Message}",
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
		public async Task<DatabaseResult<IReadOnlyList<CharacterAttributeData>>> GetAttributesAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterAttributeData>>.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.");
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var attributes = await dbContext.CharacterAttributes
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId)
					.Select(a => new CharacterAttributeData
					{
						ID = a.ID,
						CharacterID = a.CharacterID,
						TemplateID = a.TemplateID,
						Value = a.Value,
						CurrentValue = a.CurrentValue
					})
					.ToListAsync(cancellationToken);

				return DatabaseResult<IReadOnlyList<CharacterAttributeData>>.Success(attributes);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<IReadOnlyList<CharacterAttributeData>>.FromException(
					new DatabaseTimeoutException("GetCharacterAttributes", 30));
			}
			catch (PostgresException pgEx)
			{
				return DatabaseResult<IReadOnlyList<CharacterAttributeData>>.FromException(
					new DatabaseQueryException(
						"GetCharacterAttributes",
						"A database error occurred.",
						$"Database query error (SQL State: {pgEx.SqlState}): {pgEx.Message}",
						false,
						pgEx.SqlState,
						pgEx));
			}
			catch (NpgsqlException npgsqlEx)
			{
				return DatabaseResult<IReadOnlyList<CharacterAttributeData>>.FromException(
					new DatabaseConnectionException("Failed to connect to the database.", npgsqlEx));
			}
			catch (Exception ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterAttributeData>>.FromException(
					new DatabaseException("An unexpected error occurred.", ex));
			}
		}
	}
}