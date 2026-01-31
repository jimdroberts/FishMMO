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
	/// All database operations use BaseService.ExecuteTransactionAsync for:
	/// - Automatic transient failure retry
	/// - Centralized exception handling and mapping
	/// - Consistent DatabaseResult pattern
	/// </remarks>
	public sealed class CharacterPartyService : BaseService<CharacterPartyEntity>, ICharacterPartyService
	{
		private const int MaxAllowedPartyCapacity = 40;

		private sealed class SaveMembershipPrecheckResult
		{
			public bool IsAllowed { get; }
			public string? ErrorCode { get; }
			public string? ErrorMessage { get; }
			public bool NeedsCapacityCheck { get; }

			public SaveMembershipPrecheckResult(bool isAllowed, string? errorCode, string? errorMessage, bool needsCapacityCheck)
			{
				IsAllowed = isAllowed;
				ErrorCode = errorCode;
				ErrorMessage = errorMessage;
				NeedsCapacityCheck = needsCapacityCheck;
			}
		}

		/// <summary>
		/// Compiled query for checking whether a character exists and is not deleted.
		/// Returns the character ID if active, otherwise 0.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<long>> getActiveCharacterIdQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.Characters
					.AsNoTracking()
					.Where(c => c.ID == characterId && !c.Deleted)
					.Select(c => c.ID)
					.FirstOrDefault());

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
		/// Compiled query for retrieving a tracked party membership by character ID.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<CharacterPartyEntity?>> getMembershipByCharacterTrackingQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				(CharacterPartyEntity?)context.CharacterParties
					.FirstOrDefault(p => p.CharacterID == characterId));

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

			var precheck = await ExecuteReadAsync(async dbContext =>
			{
				var activeCharacterId = await getActiveCharacterIdQuery(dbContext, partyData.CharacterID, cancellationToken).ConfigureAwait(false);
				if (activeCharacterId == 0)
				{
					return new SaveMembershipPrecheckResult(false, "DB_NOT_FOUND", "Character not found or deleted.", false);
				}

				var existingPartyId = await dbContext.CharacterParties
					.AsNoTracking()
					.Where(p => p.CharacterID == partyData.CharacterID)
					.Select(p => p.PartyID)
					.FirstOrDefaultAsync(cancellationToken)
					.ConfigureAwait(false);

				var needsCapacityCheck = existingPartyId == 0 || existingPartyId != partyData.PartyID;
				if (!needsCapacityCheck)
				{
					return new SaveMembershipPrecheckResult(true, null, null, false);
				}

				var partyExists = await dbContext.Parties
					.AsNoTracking()
					.AnyAsync(p => p.ID == partyData.PartyID, cancellationToken)
					.ConfigureAwait(false);
				if (!partyExists)
				{
					return new SaveMembershipPrecheckResult(false, "NOT_FOUND", $"Party with ID {partyData.PartyID} does not exist.", true);
				}

				var currentCount = await getPartyMemberCountQuery(dbContext, partyData.PartyID, cancellationToken).ConfigureAwait(false);
				if (currentCount >= maxCapacity)
				{
					return new SaveMembershipPrecheckResult(false, "CAPACITY_EXCEEDED", $"Party has reached maximum capacity of {maxCapacity} members.", true);
				}

				return new SaveMembershipPrecheckResult(true, null, null, true);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (!precheck.IsSuccess)
			{
				return DatabaseResult.Failure(precheck.ErrorCode, precheck.ErrorMessage, precheck.IsTransient);
			}

			var precheckResult = precheck.Data;
			if (!precheckResult.IsAllowed)
			{
				return DatabaseResult.Failure(precheckResult.ErrorCode!, precheckResult.ErrorMessage!, isTransient: false);
			}

			var insertResult = await ExecuteTransactionAsync(async dbContext =>
			{
				var membership = await getMembershipByCharacterTrackingQuery(dbContext, partyData.CharacterID, cancellationToken).ConfigureAwait(false);
				if (membership == null)
				{
					membership = new CharacterPartyEntity
					{
						CharacterID = partyData.CharacterID,
						Version = partyData.Version,
						TimeCreated = DateTime.UtcNow
					};
					await dbContext.CharacterParties.AddAsync(membership, cancellationToken).ConfigureAwait(false);
				}

				ValidateVersion(membership, partyData.Version);

				membership.PartyID = partyData.PartyID;
				membership.Rank = partyData.Rank;
				membership.HealthPCT = partyData.HealthPCT;
			}).ConfigureAwait(false);

			if (insertResult.IsSuccess)
			{
				return DatabaseResult.Success();
			}

			if (insertResult.ErrorCode != "UNIQUE_VIOLATION")
			{
				return DatabaseResult.Failure(insertResult.ErrorCode, insertResult.ErrorMessage, insertResult.IsTransient);
			}

			var updateResult = await ExecuteTransactionAsync(async dbContext =>
			{
				var membership = await getMembershipByCharacterTrackingQuery(dbContext, partyData.CharacterID, cancellationToken).ConfigureAwait(false);
				if (membership == null)
				{
					throw new DatabaseEntityNotFoundException("CharacterParty", partyData.CharacterID.ToString());
				}

				ValidateVersion(membership, partyData.Version);

				membership.PartyID = partyData.PartyID;
				membership.Rank = partyData.Rank;
				membership.HealthPCT = partyData.HealthPCT;
			}).ConfigureAwait(false);

			return updateResult.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(updateResult.ErrorCode, updateResult.ErrorMessage, updateResult.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeletePartyMembershipAsync(long characterId, CancellationToken cancellationToken = default)
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
				await dbContext.Database.ExecuteSqlRawAsync(
					"DELETE FROM character_party WHERE character_id = {0}",
					new object[] { characterId },
					cancellationToken)
					.ConfigureAwait(false);
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdateRankAsync(long characterId, long partyId, byte rank, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0 || partyId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character or party ID.",
					isTransient: false);
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var membership = await dbContext.CharacterParties
					.FirstOrDefaultAsync(p => p.CharacterID == characterId && p.PartyID == partyId, cancellationToken)
					.ConfigureAwait(false);
				if (membership == null)
				{
					return;
				}
				membership.Rank = rank;
			}).ConfigureAwait(false);
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