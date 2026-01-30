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
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterAttributeEntity>>> getAttributesQuery =
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
					"Attributes collection must not be null or empty.",
					isTransient: false);
			}

			var attributeList = attributes.ToList();
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

				var templateIds = attributeList.Select(a => a.TemplateID).Distinct().ToArray();
				var existing = await dbContext.CharacterAttributes
					.Where(a => activeCharacterIdSet.Contains(a.CharacterID) && templateIds.Contains(a.TemplateID))
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				var existingByKey = new Dictionary<(long CharacterID, int TemplateID), CharacterAttributeEntity>();
				foreach (var entity in existing)
				{
					existingByKey[(entity.CharacterID, entity.TemplateID)] = entity;
				}

				foreach (var attribute in attributeList)
				{
					if (!activeCharacterIdSet.Contains(attribute.CharacterID)) continue;

					var key = (attribute.CharacterID, attribute.TemplateID);
					if (!existingByKey.TryGetValue(key, out var entity))
					{
						entity = new CharacterAttributeEntity
						{
							CharacterID = attribute.CharacterID,
							TemplateID = attribute.TemplateID,
							Version = attribute.Version,
							TimeCreated = DateTime.UtcNow
						};
						await dbContext.CharacterAttributes.AddAsync(entity, cancellationToken).ConfigureAwait(false);
						existingByKey[key] = entity;
					}

					ValidateVersion(entity, attribute.Version);
					entity.Value = attribute.Value;
					entity.CurrentValue = attribute.CurrentValue;
				}
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAttributesAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var attributeIds = await dbContext.CharacterAttributes
					.AsNoTracking()
					.Where(a => a.CharacterID == characterId)
					.Select(a => a.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				foreach (var attributeId in attributeIds)
				{
					var entity = new CharacterAttributeEntity { ID = attributeId };
					dbContext.CharacterAttributes.Remove(entity);
				}
			}).ConfigureAwait(false);
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