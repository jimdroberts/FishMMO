using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;
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
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterAttributeEntity>>> getAttributesQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterAttributes
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId && !a.Deleted)
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
					"Attributes collection must not be null or empty.",
					isTransient: false);
			}

			var attributeList = attributes.ToList();
			if (attributeList.Any(a => a.Version <= 0))
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"One or more attributes had an invalid Version. Version must be greater than 0.",
					isTransient: false);
			}

			// Prevent duplicate keys within the same batch from causing
			// "ON CONFLICT DO UPDATE command cannot affect row a second time".
			if (attributeList.Count > 1)
			{
				var deduped = new Dictionary<(long CharacterID, int TemplateID), CharacterAttributeData>();
				foreach (var attribute in attributeList)
				{
					deduped[(attribute.CharacterID, attribute.TemplateID)] = attribute;
				}

				if (deduped.Count != attributeList.Count)
				{
					attributeList = deduped.Values.ToList();
				}
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var characterIds = attributeList.Select(a => a.CharacterID).Distinct().ToArray();
				var activeCharacterIds = await dbContext.Characters
					.AsNoTracking()
					.Where(c => characterIds.Contains(c.ID) && !c.Deleted)
					.Select(c => c.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);
				var activeCharacterIdSet = new HashSet<long>(activeCharacterIds);

				var activeAttributes = attributeList.Where(a => activeCharacterIdSet.Contains(a.CharacterID)).ToList();
				if (activeAttributes.Count == 0)
				{
					return;
				}

				var now = DateTime.UtcNow;
				var characterIdArray = activeAttributes.Select(a => a.CharacterID).ToArray();
				var templateIdArray = activeAttributes.Select(a => a.TemplateID).ToArray();
				var versionArray = activeAttributes.Select(a => a.Version).ToArray();
				var valueArray = activeAttributes.Select(a => a.Value).ToArray();
				var currentValueArray = activeAttributes.Select(a => a.CurrentValue).ToArray();

				var sql = $@"
					INSERT INTO {TableName}
						(character_id, template_id, version, value, current_value, time_created, deleted, time_deleted)
					SELECT
						u.character_id,
						u.template_id,
						u.version,
						u.value,
						u.current_value,
						{{5}},
						FALSE,
						NULL
					FROM UNNEST(
						{{0}}::bigint[],
						{{1}}::integer[],
						{{2}}::bigint[],
						{{3}}::integer[],
						{{4}}::real[]
					) AS u(character_id, template_id, version, value, current_value)
					ON CONFLICT (character_id, template_id)
					DO UPDATE SET
						value = EXCLUDED.value,
						current_value = EXCLUDED.current_value,
						deleted = FALSE,
						time_deleted = NULL,
						version = EXCLUDED.version
					WHERE
						EXCLUDED.version > {TableName}.version
						OR (
							EXCLUDED.version = {TableName}.version
							AND {TableName}.value = EXCLUDED.value
							AND {TableName}.current_value = EXCLUDED.current_value
							AND {TableName}.deleted = FALSE
							AND {TableName}.time_deleted IS NULL
						);";

				await ExecuteBulkUpsertAsync(
					dbContext,
					sql,
					activeAttributes.Count,
					new object[] { characterIdArray, templateIdArray, versionArray, valueArray, currentValueArray, now },
					"One or more attributes were rejected due to a stale Version.",
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAttributesAsync(long characterId, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.",
					isTransient: false);
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid Version. Version must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sql = $@"UPDATE {TableName}
					SET deleted = TRUE, time_deleted = {{0}}, version = {{1}}
					WHERE character_id = {{2}} AND deleted = FALSE AND version < {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { now, incomingVersion, characterId }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var anyActive = await dbContext.CharacterAttributes
						.AsNoTracking()
						.AnyAsync(a => a.CharacterID == characterId && !a.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (anyActive)
					{
						throw new StaleStateException("Attribute delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterAttributeData>>> GetAttributesAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterAttributeData>>.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getAttributesQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				var attributes = entities.Select(a => new CharacterAttributeData(
					id: a.ID,
					version: a.Version,
					characterID: a.CharacterID,
					templateID: a.TemplateID,
					value: a.Value,
					currentValue: a.CurrentValue
				)).ToList();

				return (IReadOnlyList<CharacterAttributeData>)attributes;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}