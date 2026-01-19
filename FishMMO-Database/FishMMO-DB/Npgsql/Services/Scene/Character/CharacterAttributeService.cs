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

			return await ExecuteWithStrategyAsync(async (dbContext, strategy) =>
			{
				await strategy.ExecuteAsync(async () =>
				{
					var attributeList = attributes.ToList();

					// Extract arrays for bulk UPSERT
					var characterIds = attributeList.Select(a => a.CharacterID).ToArray();
					var templateIds = attributeList.Select(a => a.TemplateID).ToArray();
					var values = attributeList.Select(a => a.Value).ToArray();
					var currentValues = attributeList.Select(a => a.CurrentValue).ToArray();

					// Single bulk UPSERT using UNNEST - atomic operation, no transaction needed
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"INSERT INTO {TableName} 
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
			}, "SaveCharacterAttributes", cancellationToken);
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

			return await ExecuteWithStrategyAsync(async (dbContext, strategy) =>
			{
				await strategy.ExecuteAsync(async () =>
				{
					// Use atomic DELETE for thread safety
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"DELETE FROM {TableName} WHERE character_id = {characterId}",
						cancellationToken);
				});
			}, "DeleteCharacterAttributes", cancellationToken);
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

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				var entities = await GetAttributesQuery(dbContext, characterId, cancellationToken);
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
	}
}