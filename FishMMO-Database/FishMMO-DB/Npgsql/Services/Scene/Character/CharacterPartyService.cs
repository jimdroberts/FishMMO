using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service for managing character party membership in the database.
	/// Provides async operations for CRUD operations on character party data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	/// <remarks>
	/// This service manages character party memberships including:
	/// - Party membership save/update with atomic UPSERT operations
	/// - Rank updates
	/// - Party membership deletion
	/// - Party membership and member retrieval
	/// 
	/// Database operations are executed via the BaseService execution wrappers for:
	/// - Automatic transient failure retry
	/// - Centralized exception handling and mapping
	/// - Consistent DatabaseResult pattern
	/// 
	/// When a write requires multiple database statements, it should be wrapped in
	/// <see cref="BaseService{TEntity}.ExecuteTransactionAsync"/>. Single-statement SQL
	/// operations (including CTE-based UPSERT/DELETE/UPDATE) are executed atomically without
	/// requiring an explicit transaction wrapper.
	/// </remarks>
	public sealed class CharacterPartyService : BaseService<CharacterPartyEntity>, ICharacterPartyService
	{
		private const int MaxAllowedPartyCapacity = 40;

		/// <summary>
		/// Compiled query for retrieving party membership by character ID.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<CharacterPartyEntity?>> getPartyMembershipQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterParties
					.AsNoTracking()
					.FirstOrDefault(p => p.CharacterID == characterId));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for retrieving all party members.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterPartyEntity>>> getPartyMembersQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long partyId, CancellationToken ct) =>
				context.CharacterParties
					.AsNoTracking()
					.Where(p => p.PartyID == partyId)
					.ToList());

		/// <summary>
		/// Compiled query for counting party members.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<int>> getPartyMemberCountQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long partyId, CancellationToken ct) =>
				context.CharacterParties
					.AsNoTracking()
					.Where(p => p.PartyID == partyId)
					.Count());

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterPartyService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterPartyService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SavePartyMembershipAsync(CharacterPartyData partyData, int maxCapacity, CancellationToken cancellationToken = default)
		{
			if (partyData.CharacterID <= 0 || partyData.PartyID <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character or party ID.",
					isTransient: false);
			}

			if (maxCapacity <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid max capacity. Max capacity must be greater than 0.",
					isTransient: false);
			}

			if (maxCapacity > MaxAllowedPartyCapacity)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					$"Invalid max capacity. Max capacity must not exceed {MaxAllowedPartyCapacity}.",
					isTransient: false);
			}

			if (partyData.Version <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid version. Version must be greater than 0.",
					isTransient: false);
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var characterTableName = dbContext.GetTableName<CharacterEntity>();
				var partyTableName = dbContext.GetTableName<PartyEntity>();

				var sql = $@"
					WITH
					active_character AS (
						SELECT id
						FROM {characterTableName}
						WHERE id = {{0}} AND deleted = FALSE
					),
					party_row AS (
						SELECT id
						FROM {partyTableName}
						WHERE id = {{1}}
						FOR UPDATE
					),
					existing_membership AS (
						SELECT party_id, version, rank, health_pct
						FROM {TableName}
						WHERE character_id = {{0}}
					),
					capacity_ok AS (
						SELECT CASE
							WHEN NOT EXISTS (SELECT 1 FROM party_row) THEN FALSE
							WHEN EXISTS (SELECT 1 FROM existing_membership WHERE party_id = {{1}}) THEN TRUE
							WHEN (SELECT COUNT(*) FROM {TableName} WHERE party_id = {{1}}) < {{2}} THEN TRUE
							ELSE FALSE
						END AS ok
					),
					upserted AS (
						INSERT INTO {TableName} (character_id, party_id, rank, health_pct, version, time_created)
						SELECT {{0}}, {{1}}, {{3}}, {{4}}, {{5}}, CURRENT_TIMESTAMP
						FROM active_character, capacity_ok
						WHERE capacity_ok.ok = TRUE
						ON CONFLICT (character_id)
						DO UPDATE SET
							party_id = EXCLUDED.party_id,
							rank = EXCLUDED.rank,
							health_pct = EXCLUDED.health_pct,
							version = EXCLUDED.version
						WHERE
							EXCLUDED.version > {TableName}.version
							OR (
								EXCLUDED.version = {TableName}.version
								AND {TableName}.party_id = EXCLUDED.party_id
								AND {TableName}.rank = EXCLUDED.rank
								AND {TableName}.health_pct = EXCLUDED.health_pct
							)
						RETURNING 1
					)
					SELECT
						CASE
							WHEN NOT EXISTS (SELECT 1 FROM active_character) THEN 1
							WHEN NOT EXISTS (SELECT 1 FROM party_row) THEN 2
							WHEN NOT (SELECT ok FROM capacity_ok) THEN 3
							WHEN EXISTS (SELECT 1 FROM upserted) THEN 0
							ELSE 4
						END AS value";

				var status = await dbContext.Set<SqlIntValue>()
					.FromSqlRaw(
						sql,
						partyData.CharacterID,
						partyData.PartyID,
						maxCapacity,
						partyData.Rank,
						partyData.HealthPCT,
						partyData.Version)
					.AsNoTracking()
					.SingleAsync(cancellationToken)
					.ConfigureAwait(false);

				return status.Value;
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (!result.IsSuccess)
			{
				return DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
			}

			switch (result.Data)
			{
				case 0:
					return DatabaseResult.Success();
				case 1:
					return DatabaseResult.Failure("DB_NOT_FOUND", "Character not found or deleted.", isTransient: false);
				case 2:
					return DatabaseResult.Failure("NOT_FOUND", $"Party with ID {partyData.PartyID} does not exist.", isTransient: false);
				case 3:
					return DatabaseResult.Failure("CAPACITY_EXCEEDED", $"Party has reached maximum capacity of {maxCapacity} members.", isTransient: false);
				case 4:
					return DatabaseResult.Failure("STALE_STATE", "Party membership update rejected due to a stale Version.", isTransient: false);
				default:
					return DatabaseResult.Failure("DATABASE_ERROR", "A database error occurred.", isTransient: true);
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeletePartyMembershipAsync(long characterId, long incomingVersion, CancellationToken cancellationToken = default)
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
				var deleteSql = $@"DELETE FROM {TableName}
					WHERE character_id = {{0}} AND version < {{1}}";

				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(deleteSql, new object[] { characterId, incomingVersion }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var existing = await dbContext.CharacterParties
						.AsNoTracking()
						.FirstOrDefaultAsync(p => p.CharacterID == characterId, cancellationToken)
						.ConfigureAwait(false);

					if (existing != null)
					{
						throw new StaleStateException("Party membership delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdateRankAsync(long characterId, long partyId, byte rank, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0 || partyId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character or party ID.",
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
				var updateSql = $@"UPDATE {TableName}
					SET rank = {{0}}, version = {{1}}
					WHERE character_id = {{2}} AND party_id = {{3}} AND version < {{1}}";

				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(updateSql, new object[] { rank, incomingVersion, characterId, partyId }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var existing = await dbContext.CharacterParties
						.AsNoTracking()
						.FirstOrDefaultAsync(p => p.CharacterID == characterId && p.PartyID == partyId, cancellationToken)
						.ConfigureAwait(false);

					if (existing == null)
					{
						throw new DatabaseEntityNotFoundException("CharacterParty", characterId.ToString());
					}

					if (existing.Version == incomingVersion && existing.Rank == rank)
					{
						return;
					}

					throw new StaleStateException("Party rank update rejected due to a stale Version.");
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterPartyData?>> GetPartyMembershipAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<CharacterPartyData?>.Failure(
					"VALIDATION_ERROR",
					"Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entity = await getPartyMembershipQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				if (entity == null)
					return (CharacterPartyData?)null;

				return (CharacterPartyData?)new CharacterPartyData(
					id: entity.ID,
					version: entity.Version,
					characterID: entity.CharacterID,
					partyID: entity.PartyID,
					rank: entity.Rank,
					healthPCT: entity.HealthPCT
				);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterPartyData>>> GetPartyMembersAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterPartyData>>.Failure(
					"VALIDATION_ERROR",
					"Party ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getPartyMembersQuery(dbContext, partyId, cancellationToken).ConfigureAwait(false);
				var members = entities.Select(p => new CharacterPartyData(
					id: p.ID,
					version: p.Version,
					characterID: p.CharacterID,
					partyID: p.PartyID,
					rank: p.Rank,
					healthPCT: p.HealthPCT
				)).ToList();

				return (IReadOnlyList<CharacterPartyData>)members;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> GetPartyMemberCountAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId <= 0)
			{
				return DatabaseResult<int>.Failure(
					"VALIDATION_ERROR",
					"Party ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteReadAsync(async dbContext =>
				await getPartyMemberCountQuery(dbContext, partyId, cancellationToken).ConfigureAwait(false),
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}