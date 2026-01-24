using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Entities;
using Microsoft.EntityFrameworkCore;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service for managing character attributes in the database.
	/// Provides async operations for CRUD operations on character attribute data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	public sealed class CharacterAttributeService : BaseService<CharacterAttributeEntity>, ICharacterAttributeService
	{
		/// <summary>
		/// Compiled query for retrieving character attributes (hot path for character load).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterAttributeEntity>>> GetAttributesQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterAttributes
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId)
					.ToList());

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterAttributeService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterAttributeService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
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

			var attributeList = attributes.ToList();

			// Extract arrays for bulk UPSERT
			var characterIds = attributeList.Select(a => a.CharacterID).ToArray();
			var templateIds = attributeList.Select(a => a.TemplateID).ToArray();
			var values = attributeList.Select(a => a.Value).ToArray();
			var currentValues = attributeList.Select(a => a.CurrentValue).ToArray();

			// Single bulk UPSERT using UNNEST - atomic operation, no transaction needed
			var result = await ExecuteRawSqlAsync(
				$@"INSERT INTO {TableName} 
				(character_id, template_id, value, current_value)
				SELECT * FROM UNNEST(
					{{0}}::bigint[],
					{{1}}::int[],
					{{2}}::int[],
					{{3}}::float4[]
				)
				ON CONFLICT (character_id, template_id) 
				DO UPDATE SET 
					value = EXCLUDED.value,
					current_value = EXCLUDED.current_value",
				"SaveCharacterAttributes",
				new object[] { characterIds, templateIds, values, currentValues },
				entityName: "CharacterAttribute",
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
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

			// Use atomic DELETE for thread safety
			var result = await ExecuteRawSqlAsync(
				$@"DELETE FROM {TableName} WHERE character_id = {{0}}",
				"DeleteCharacterAttributes",
				new object[] { characterId },
				entityName: "CharacterAttribute",
				entityId: characterId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
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

			return await ExecuteAsync(async (dbContext, ct) =>
			{
				var entities = await GetAttributesQuery(dbContext, characterId, ct);
				var attributes = entities.Select(a => new CharacterAttributeData(
					id: a.ID,
					characterID: a.CharacterID,
					templateID: a.TemplateID,
					value: a.Value,
					currentValue: a.CurrentValue
				)).ToList();

				return (IReadOnlyList<CharacterAttributeData>)attributes;
			}, "GetCharacterAttributes", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> IncrementAttributeAsync(
			long characterId,
			int templateId,
			int valueDelta,
			float currentValueDelta,
			bool allowNegative = false,
			CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.");
			}

			// Build atomic increment query with optional negative value check
			// Uses UPSERT pattern: insert if not exists, update if exists
			// The WHERE clause on the UPDATE prevents negative values if allowNegative is false
			var sql = allowNegative
				? $@"INSERT INTO {TableName} (character_id, template_id, value, current_value)
				   VALUES ({{0}}, {{1}}, {{2}}, {{3}})
				   ON CONFLICT (character_id, template_id)
				   DO UPDATE SET
					   value = {TableName}.value + {{2}},
					   current_value = {TableName}.current_value + {{3}}"
				: $@"INSERT INTO {TableName} (character_id, template_id, value, current_value)
				   VALUES ({{0}}, {{1}}, {{2}}, {{3}})
				   ON CONFLICT (character_id, template_id)
				   DO UPDATE SET
					   value = {TableName}.value + {{2}},
					   current_value = {TableName}.current_value + {{3}}
				   WHERE {TableName}.value + {{2}} >= 0 AND {TableName}.current_value + {{3}} >= 0";

			var result = await ExecuteRawSqlAsync(
				sql,
				"IncrementCharacterAttribute",
				new object[] { characterId, templateId, valueDelta, currentValueDelta },
				entityName: "CharacterAttribute",
				entityId: characterId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

			if (result.IsSuccess)
			{
				if (!allowNegative && result.Data == 0)
				{
					return DatabaseResult.Failure(
						"VALIDATION_ERROR",
						"Operation would result in negative attribute value.",
						isTransient: false);
				}

				return DatabaseResult.Success();
			}

			return DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}
	}
}