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
	/// All database operations use BaseService.ExecuteWithStrategyAsync for:
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
			var result = await ExecuteInTransactionAsync(async (dbContext, transaction) =>
			{
				// Check if character is already in this party (UPDATE case)
				var existingMembership = await dbContext.CharacterParties
					.AsNoTracking()
					.FirstOrDefaultAsync(p => p.CharacterID == partyData.CharacterID, cancellationToken);

				// Refined check: determine if capacity validation is needed
				bool needsCapacityCheck = existingMembership == null || existingMembership.PartyID != partyData.PartyID;

				if (needsCapacityCheck)
				{
					// Lock party row to prevent concurrent capacity violations
					// Using SELECT ... FOR UPDATE to acquire row-level lock
					var partyEntity = await dbContext.Parties
						.FromSqlInterpolated($"SELECT * FROM parties WHERE id = {partyData.PartyID} FOR UPDATE")
						.FirstOrDefaultAsync(cancellationToken);

					if (partyEntity == null)
					{
						return DatabaseResult.Failure(
							"NOT_FOUND",
							$"Party with ID {partyData.PartyID} does not exist.");
					}

					// Count current members (lock is held, preventing concurrent inserts)
					var currentCount = await dbContext.CharacterParties
						.Where(p => p.PartyID == partyData.PartyID)
						.CountAsync(cancellationToken);

					if (currentCount >= maxCapacity)
					{
						return DatabaseResult.Failure(
							"CAPACITY_EXCEEDED",
							$"Party has reached maximum capacity of {maxCapacity} members.");
					}
				}

				// Perform UPSERT
				await dbContext.Database.ExecuteSqlInterpolatedAsync(
					$@"INSERT INTO {TableName} 
					   (character_id, party_id, rank, health_pct)
					   VALUES ({partyData.CharacterID}, {partyData.PartyID}, {partyData.Rank}, {partyData.HealthPCT})
					   ON CONFLICT (character_id) 
					   DO UPDATE SET 
						   party_id = EXCLUDED.party_id,
						   rank = EXCLUDED.rank,
						   health_pct = EXCLUDED.health_pct",
					cancellationToken);

				return DatabaseResult.Success();
			}, "SavePartyMembership", cancellationToken);

			return result.IsSuccess ? result.Data : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeletePartyMembershipAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			var result = await ExecuteSqlAsync(
				$@"DELETE FROM {TableName} WHERE character_id = {characterId}",
				"DeletePartyMembership",
				entityName: "CharacterParty",
				entityId: characterId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdateRankAsync(long characterId, long partyId, byte rank, CancellationToken cancellationToken = default)
		{
			if (characterId == 0 || partyId == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character or party ID");
			}

			var result = await ExecuteSqlAsync(
				$@"UPDATE {TableName} 
					SET rank = {rank} 
					WHERE character_id = {characterId} AND party_id = {partyId}",
				"UpdateRank",
				entityName: "CharacterParty",
				entityId: characterId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterPartyData?>> GetPartyMembershipAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<CharacterPartyData?>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				var entity = await GetPartyMembershipQuery(dbContext, characterId, cancellationToken);
				if (entity == null)
					return (CharacterPartyData?)null;

				return (CharacterPartyData?)new CharacterPartyData(
					id: entity.ID,
					characterID: entity.CharacterID,
					partyID: entity.PartyID,
					rank: entity.Rank,
					healthPCT: entity.HealthPCT
				);
			}, "GetPartyMembership", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterPartyData>>> GetPartyMembersAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterPartyData>>.Failure("VALIDATION_ERROR", "Invalid party ID");
			}

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				var entities = await GetPartyMembersQuery(dbContext, partyId, cancellationToken);
				var members = entities.Select(p => new CharacterPartyData(
					id: p.ID,
					characterID: p.CharacterID,
					partyID: p.PartyID,
					rank: p.Rank,
					healthPCT: p.HealthPCT
				)).ToList();

				return (IReadOnlyList<CharacterPartyData>)members;
			}, "GetPartyMembers", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> GetPartyMemberCountAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId == 0)
			{
				return DatabaseResult<int>.Failure("VALIDATION_ERROR", "Invalid party ID");
			}

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				return await GetPartyMemberCountQuery(dbContext, partyId, cancellationToken);
			}, "GetPartyMemberCount", cancellationToken);
		}
	}
}