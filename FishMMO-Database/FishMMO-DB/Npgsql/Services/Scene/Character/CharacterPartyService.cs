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
	public sealed class CharacterPartyService : IdempotentBaseService<CharacterPartyEntity>, ICharacterPartyService
	{
		private const int MaxAllowedPartyCapacity = 40;

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
		public async Task<DatabaseResult> SavePartyMembershipAsync(CharacterPartyData partyData, int maxCapacity, Guid requestId, CancellationToken cancellationToken = default)
		{
			if (partyData.CharacterID == 0 || partyData.PartyID == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character or party ID");
			}

			if (maxCapacity <= 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid max capacity. Max capacity must be greater than 0.");
			}

			if (maxCapacity > MaxAllowedPartyCapacity)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					$"Invalid max capacity. Max capacity must not exceed {MaxAllowedPartyCapacity}.");
			}

			if (requestId == Guid.Empty)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "RequestId is required for idempotent party membership save.");
			}

			// Use transaction to atomically check capacity and insert/update membership
			var result = await ExecuteIdempotentResultAsync(
				requestId,
				scopeId: partyData.CharacterID,
				operationName: "SavePartyMembership",
				operation: async (dbContext, transaction, ct) =>
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
					var membershipTableName = dbContext.GetTableName<CharacterPartyEntity>();

					// Atomic capacity check with proper locking to prevent race conditions
					// Uses FOR UPDATE on party and FOR SHARE on member count to ensure consistent read
					// Lock ordering: character membership (already locked) -> party -> member count
					var capacityResult = await dbContext.Parties
						.FromSqlRaw($@"
							SELECT p.*
							FROM {partyTableName} p
							CROSS JOIN LATERAL (
								SELECT COUNT(*) as member_count
								FROM {membershipTableName}
								WHERE party_id = {{0}}
								FOR SHARE
							) mc
							WHERE p.id = {{0}}
								AND mc.member_count < {{1}}
							FOR UPDATE",
							partyData.PartyID,
							maxCapacity)
						.FirstOrDefaultAsync(ct).ConfigureAwait(false);

					if (capacityResult == null)
					{
						// Either party doesn't exist or capacity exceeded - check which
						var partyExists = await dbContext.Parties
							.AsNoTracking()
							.AnyAsync(p => p.ID == partyData.PartyID, ct).ConfigureAwait(false);

						if (!partyExists)
						{
							return DatabaseResult.Failure(
								"NOT_FOUND",
								$"Party with ID {partyData.PartyID} does not exist.");
						}

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
			},
				cancellationToken).ConfigureAwait(false);

			return result;
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