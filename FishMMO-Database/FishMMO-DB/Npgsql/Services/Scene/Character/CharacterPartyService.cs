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
	/// All database operations use BaseService.ExecuteAsync for:
	/// - Automatic execution strategy with transient failure retry
	/// - Centralized exception handling and mapping
	/// - Consistent DatabaseResult pattern
	/// </remarks>
	public sealed class CharacterPartyService : BaseService<CharacterPartyEntity>, ICharacterPartyService
	{
		/// <summary>
		/// Compiled query for retrieving party membership by character ID.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<CharacterPartyEntity?>> GetPartyMembershipQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterParties
					.AsNoTracking()
					.FirstOrDefault(p => p.CharacterID == characterId));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for retrieving all party members.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterPartyEntity>>> GetPartyMembersQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long partyId, CancellationToken ct) =>
				context.CharacterParties
					.AsNoTracking()
					.Where(p => p.PartyID == partyId)
					.ToList());

		/// <summary>
		/// Compiled query for counting party members.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<int>> GetPartyMemberCountQuery =
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
			if (partyData.CharacterID == 0 || partyData.PartyID == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character or party ID");
			}

			if (maxCapacity <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid max capacity. Max capacity must be greater than 0.");
			}

			// Use transaction to atomically check capacity and insert/update membership
			var result = await ExecuteTransactionAsync(async (dbContext, transaction, ct) =>
			{
				var charactersTableName = dbContext.GetTableName<CharacterEntity>();
				var activeCharacter = await dbContext.Characters
					.FromSqlRaw($@"SELECT * FROM {charactersTableName} WHERE id = {{0}} AND deleted = FALSE FOR KEY SHARE", partyData.CharacterID)
					.AsNoTracking()
					.FirstOrDefaultAsync(ct)
					.ConfigureAwait(false);
				if (activeCharacter == null)
				{
					return DatabaseResult.Failure(
						"DB_NOT_FOUND",
						"Character not found or deleted.");
				}

				// Lock character's membership row FIRST to establish consistent lock ordering
				// This prevents deadlocks and ensures atomicity across concurrent requests
				var existingMembership = await dbContext.CharacterParties
					.FromSqlRaw($"SELECT * FROM {TableName} WHERE character_id = {{0}} FOR UPDATE", partyData.CharacterID)
					.FirstOrDefaultAsync(ct).ConfigureAwait(false);

				// Refined check: determine if capacity validation is needed
				bool needsCapacityCheck = existingMembership == null || existingMembership.PartyID != partyData.PartyID;

				if (needsCapacityCheck)
				{
					var partyTableName = dbContext.GetTableName<PartyEntity>();

					// Then lock party row to prevent concurrent capacity violations
					// Lock ordering: character membership -> party (prevents race conditions)
					var partyEntity = await dbContext.Parties
						.FromSqlRaw($"SELECT * FROM {partyTableName} WHERE id = {{0}} FOR UPDATE", partyData.PartyID)
						.FirstOrDefaultAsync(ct).ConfigureAwait(false);

					if (partyEntity == null)
					{
						return DatabaseResult.Failure(
							"NOT_FOUND",
							$"Party with ID {partyData.PartyID} does not exist.");
					}

					// Count current members (lock is held, preventing concurrent inserts)
					var currentCount = await dbContext.CharacterParties
						.Where(p => p.PartyID == partyData.PartyID)
						.CountAsync(ct).ConfigureAwait(false);

					if (currentCount >= maxCapacity)
					{
						return DatabaseResult.Failure(
							"CAPACITY_EXCEEDED",
							$"Party has reached maximum capacity of {maxCapacity} members.");
					}
				}

				// Perform UPSERT
				await dbContext.Database.ExecuteSqlRawAsync(
					$@"INSERT INTO {TableName} 
					   (character_id, party_id, rank, health_pct, time_created)
					   VALUES ({{0}}, {{1}}, {{2}}, {{3}}, CURRENT_TIMESTAMP)
					   ON CONFLICT (character_id) 
					   DO UPDATE SET 
						   party_id = EXCLUDED.party_id,
						   rank = EXCLUDED.rank,
						   health_pct = EXCLUDED.health_pct",
					new object[] { partyData.CharacterID, partyData.PartyID, partyData.Rank, partyData.HealthPCT },
					ct).ConfigureAwait(false);

				return DatabaseResult.Success();
			}, "SavePartyMembership", cancellationToken).ConfigureAwait(false);

			return result.IsSuccess ? result.Data : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeletePartyMembershipAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			var result = await ExecuteRawSqlAsync(
				$@"DELETE FROM {TableName} WHERE character_id = {{0}}",
				"DeletePartyMembership",
				new object[] { characterId },
				entityName: "CharacterParty",
				entityId: characterId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdateRankAsync(long characterId, long partyId, byte rank, CancellationToken cancellationToken = default)
		{
			if (characterId == 0 || partyId == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character or party ID");
			}

			var result = await ExecuteRawSqlAsync(
				$@"UPDATE {TableName} 
					SET rank = {{0}} 
					WHERE character_id = {{1}} AND party_id = {{2}}",
				"UpdateRank",
				new object[] { rank, characterId, partyId },
				entityName: "CharacterParty",
				entityId: characterId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterPartyData?>> GetPartyMembershipAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<CharacterPartyData?>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			return await ExecuteAsync(async (dbContext, ct) =>
			{
				var entity = await GetPartyMembershipQuery(dbContext, characterId, ct).ConfigureAwait(false);
				if (entity == null)
					return (CharacterPartyData?)null;

				return (CharacterPartyData?)new CharacterPartyData(
					id: entity.ID,
					characterID: entity.CharacterID,
					partyID: entity.PartyID,
					rank: entity.Rank,
					healthPCT: entity.HealthPCT
				);
			}, "GetPartyMembership", cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterPartyData>>> GetPartyMembersAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterPartyData>>.Failure("VALIDATION_ERROR", "Invalid party ID");
			}

			return await ExecuteAsync(async (dbContext, ct) =>
			{
				var entities = await GetPartyMembersQuery(dbContext, partyId, ct).ConfigureAwait(false);
				var members = entities.Select(p => new CharacterPartyData(
					id: p.ID,
					characterID: p.CharacterID,
					partyID: p.PartyID,
					rank: p.Rank,
					healthPCT: p.HealthPCT
				)).ToList();

				return (IReadOnlyList<CharacterPartyData>)members;
			}, "GetPartyMembers", cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> GetPartyMemberCountAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId == 0)
			{
				return DatabaseResult<int>.Failure("VALIDATION_ERROR", "Invalid party ID");
			}

			return await ExecuteAsync(async (dbContext, ct) =>
			{
				return await GetPartyMemberCountQuery(dbContext, partyId, ct).ConfigureAwait(false);
			}, "GetPartyMemberCount", cancellationToken).ConfigureAwait(false);
		}
	}
}