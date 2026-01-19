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
		public async Task<DatabaseResult> SavePartyMembershipAsync(CharacterPartyData partyData, CancellationToken cancellationToken = default)
		{
			if (partyData.CharacterID == 0 || partyData.PartyID == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character or party ID");
			}

			var result = await ExecuteWithStrategyAsync<int>(async (dbContext, strategy) =>
			{
				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					// Use PostgreSQL UPSERT for atomic insert-or-update
					return await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"INSERT INTO {TableName} 
					   (character_id, party_id, rank, health_pct)
					   VALUES ({partyData.CharacterID}, {partyData.PartyID}, {partyData.Rank}, {partyData.HealthPCT})
					   ON CONFLICT (character_id) 
					   DO UPDATE SET 
					       party_id = EXCLUDED.party_id,
					       rank = EXCLUDED.rank,
					       health_pct = EXCLUDED.health_pct",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					throw new DatabaseQueryException(
						"SavePartyMembership",
						"No rows affected during party membership save.",
						"SAVE_FAILED",
						false);
				}

				return rowsAffected;
			}, "SavePartyMembership", cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeletePartyMembershipAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				// Use atomic DELETE for thread safety
				await dbContext.Database.ExecuteSqlInterpolatedAsync(
					$@"DELETE FROM {TableName} WHERE character_id = {characterId}",
					cancellationToken);
			}, "DeletePartyMembership", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdateRankAsync(long characterId, long partyId, byte rank, CancellationToken cancellationToken = default)
		{
			if (characterId == 0 || partyId == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character or party ID");
			}

			var result = await ExecuteWithStrategyAsync<int>(async (dbContext, strategy) =>
			{
				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					// Atomic update without loading entity
					return await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"UPDATE {TableName} 
						SET rank = {rank} 
						WHERE character_id = {characterId} AND party_id = {partyId}",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					throw new DatabaseEntityNotFoundException(
						"PartyMembership",
						$"character_id={characterId}, party_id={partyId}",
						"Membership not found");
				}

				return rowsAffected;
			}, "UpdateRank", cancellationToken);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage);
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