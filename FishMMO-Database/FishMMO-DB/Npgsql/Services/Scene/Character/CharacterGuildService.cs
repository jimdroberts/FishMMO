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
	/// Service for managing character guild membership in the database.
	/// Provides async operations for CRUD operations on character guild membership data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	/// <remarks>
	/// This service manages character guild membership including:
	/// - Guild membership save/update with atomic UPSERT operations
	/// - Rank updates
	/// - Membership deletion
	/// - Membership retrieval (individual and guild-wide)
	/// - Member count queries
	/// 
	/// All database exceptions are caught and wrapped in appropriate DatabaseException types:
	/// - OperationCanceledException → DatabaseOperationCanceledException
	/// - PostgresException (23503) → DatabaseConstraintException (Foreign key violation)
	/// - NpgsqlException → DatabaseConnectionException
	/// - DbUpdateException → DatabaseQueryException
	/// - Exception → DatabaseQueryException
	/// 
	/// Methods return DatabaseResult to provide structured error handling
	/// without throwing exceptions to calling code.
	/// </remarks>
	public sealed class CharacterGuildService : BaseService<CharacterGuildEntity>, ICharacterGuildService
	{
		private const int MaxAllowedGuildCapacity = 256;

		private sealed class SaveMembershipPrecheckResult
		{
			public bool IsAllowed { get; }
			public string? ErrorCode { get; }
			public string? ErrorMessage { get; }

			public SaveMembershipPrecheckResult(bool isAllowed, string? errorCode, string? errorMessage)
			{
				IsAllowed = isAllowed;
				ErrorCode = errorCode;
				ErrorMessage = errorMessage;
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
		/// Compiled query for retrieving guild membership by character ID.
		/// </summary>
#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<CharacterGuildEntity?>> getGuildMembershipQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterGuilds
					.AsNoTracking()
					.FirstOrDefault(g => g.CharacterID == characterId));
#pragma warning restore CS8619

		/// <summary>
		/// Compiled query for retrieving all guild members.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterGuildEntity>>> getGuildMembersQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long guildId, CancellationToken ct) =>
				context.CharacterGuilds
					.AsNoTracking()
					.Where(g => g.GuildID == guildId)
					.ToList());

		/// <summary>
		/// Compiled query for counting guild members.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<int>> getGuildMemberCountQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long guildId, CancellationToken ct) =>
				context.CharacterGuilds
					.AsNoTracking()
					.Where(g => g.GuildID == guildId)
					.Count());

		/// <summary>
		/// Compiled query for retrieving a tracked guild membership by character ID.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<CharacterGuildEntity?>> getMembershipByCharacterTrackingQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				(CharacterGuildEntity?)context.CharacterGuilds
					.FirstOrDefault(g => g.CharacterID == characterId));

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterGuildService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterGuildService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveGuildMembershipAsync(CharacterGuildData guildData, int maxCapacity, CancellationToken cancellationToken = default)
		{
			if (guildData.CharacterID == 0 || guildData.GuildID == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID or guild ID. Both must be greater than 0.");
			}

			if (maxCapacity <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid max capacity. Max capacity must be greater than 0.");
			}

			if (maxCapacity > MaxAllowedGuildCapacity)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					$"Invalid max capacity. Max capacity must not exceed {MaxAllowedGuildCapacity}.");
			}

			var precheck = await ExecuteMirrorAsync(async dbContext =>
			{
				var activeCharacterId = await getActiveCharacterIdQuery(dbContext, guildData.CharacterID, cancellationToken).ConfigureAwait(false);
				if (activeCharacterId == 0)
				{
					return new SaveMembershipPrecheckResult(false, "DB_NOT_FOUND", "Character not found or deleted.");
				}

				var existingGuildId = await dbContext.CharacterGuilds
					.AsNoTracking()
					.Where(g => g.CharacterID == guildData.CharacterID)
					.Select(g => g.GuildID)
					.FirstOrDefaultAsync(cancellationToken)
					.ConfigureAwait(false);

				var needsCapacityCheck = existingGuildId == 0 || existingGuildId != guildData.GuildID;
				if (!needsCapacityCheck)
				{
					return new SaveMembershipPrecheckResult(true, null, null);
				}

				var guildExists = await dbContext.Guilds
					.AsNoTracking()
					.AnyAsync(g => g.ID == guildData.GuildID, cancellationToken)
					.ConfigureAwait(false);
				if (!guildExists)
				{
					return new SaveMembershipPrecheckResult(false, "NOT_FOUND", $"Guild with ID {guildData.GuildID} does not exist.");
				}

				var currentCount = await getGuildMemberCountQuery(dbContext, guildData.GuildID, cancellationToken).ConfigureAwait(false);
				if (currentCount >= maxCapacity)
				{
					return new SaveMembershipPrecheckResult(false, "CAPACITY_EXCEEDED", $"Guild has reached maximum capacity of {maxCapacity} members.");
				}

				return new SaveMembershipPrecheckResult(true, null, null);
			}).ConfigureAwait(false);

			if (!precheck.IsSuccess)
			{
				return DatabaseResult.Failure(precheck.ErrorCode, precheck.ErrorMessage, precheck.IsTransient);
			}

			var precheckResult = precheck.Data;
			if (!precheckResult.IsAllowed)
			{
				return DatabaseResult.Failure(precheckResult.ErrorCode!, precheckResult.ErrorMessage!, isTransient: false);
			}

			var insertResult = await ExecuteMirrorAsync(async dbContext =>
			{
				var membership = await getMembershipByCharacterTrackingQuery(dbContext, guildData.CharacterID, cancellationToken).ConfigureAwait(false);
				if (membership == null)
				{
					membership = new CharacterGuildEntity
					{
						CharacterID = guildData.CharacterID,
						Version = guildData.Version,
						TimeCreated = DateTime.UtcNow
					};
					await dbContext.CharacterGuilds.AddAsync(membership, cancellationToken).ConfigureAwait(false);
				}

				ValidateVersion(membership, guildData.Version);

				membership.GuildID = guildData.GuildID;
				membership.Rank = guildData.Rank;
				membership.Location = guildData.Location;
			}).ConfigureAwait(false);

			if (insertResult.IsSuccess)
			{
				return DatabaseResult.Success();
			}

			if (insertResult.ErrorCode != "UNIQUE_VIOLATION")
			{
				return DatabaseResult.Failure(insertResult.ErrorCode, insertResult.ErrorMessage, insertResult.IsTransient);
			}

			var updateResult = await ExecuteMirrorAsync(async dbContext =>
			{
				var membership = await getMembershipByCharacterTrackingQuery(dbContext, guildData.CharacterID, cancellationToken).ConfigureAwait(false);
				if (membership == null)
				{
					throw new DatabaseEntityNotFoundException("CharacterGuild", guildData.CharacterID.ToString());
				}

				ValidateVersion(membership, guildData.Version);

				membership.GuildID = guildData.GuildID;
				membership.Rank = guildData.Rank;
				membership.Location = guildData.Location;
			}).ConfigureAwait(false);

			return updateResult.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(updateResult.ErrorCode, updateResult.ErrorMessage, updateResult.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdateRankAsync(long characterId, long guildId, byte rank, CancellationToken cancellationToken = default)
		{
			if (characterId == 0 || guildId == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID or guild ID. Both must be greater than 0.");
			}

			var result = await ExecuteMirrorAsync(async dbContext =>
			{
				var membership = await dbContext.CharacterGuilds
					.FirstOrDefaultAsync(g => g.CharacterID == characterId && g.GuildID == guildId, cancellationToken)
					.ConfigureAwait(false);

				if (membership != null)
				{
					membership.Rank = rank;
				}
			}).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteGuildMembershipAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.");
			}

			var result = await ExecuteMirrorAsync(async dbContext =>
			{
				var memberships = await dbContext.CharacterGuilds
					.Where(g => g.CharacterID == characterId)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				if (memberships.Count == 0)
				{
					return;
				}

				dbContext.CharacterGuilds.RemoveRange(memberships);
			}).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterGuildData?>> GetGuildMembershipAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<CharacterGuildData?>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.");
			}

			var result = await ExecuteMirrorAsync<CharacterGuildData?>(async dbContext =>
			{
				var entity = await getGuildMembershipQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				if (entity == null)
				{
					return null;
				}

				return new CharacterGuildData(
					id: entity.ID,
					version: entity.Version,
					characterID: entity.CharacterID,
					guildID: entity.GuildID,
					rank: entity.Rank,
					location: entity.Location);
			}).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<CharacterGuildData?>.Success(result.Data)
				: DatabaseResult<CharacterGuildData?>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterGuildData>>> GetGuildMembersAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterGuildData>>.Failure(
					"VALIDATION_ERROR",
					"Invalid guild ID. Guild ID must be greater than 0.");
			}

			var result = await ExecuteMirrorAsync(async dbContext =>
			{
				var entities = await getGuildMembersQuery(dbContext, guildId, cancellationToken).ConfigureAwait(false);
				var members = entities.Select(g => new CharacterGuildData(
					id: g.ID,
					version: g.Version,
					characterID: g.CharacterID,
					guildID: g.GuildID,
					rank: g.Rank,
					location: g.Location)).ToList();

				return (IReadOnlyList<CharacterGuildData>)members;
			}).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<IReadOnlyList<CharacterGuildData>>.Success(result.Data)
				: DatabaseResult<IReadOnlyList<CharacterGuildData>>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> GetGuildMemberCountAsync(long guildId, CancellationToken cancellationToken = default)
		{
			if (guildId == 0)
			{
				return DatabaseResult<int>.Failure(
					"VALIDATION_ERROR",
					"Invalid guild ID. Guild ID must be greater than 0.");
			}

			var result = await ExecuteMirrorAsync(async dbContext =>
				await getGuildMemberCountQuery(dbContext, guildId, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<int>.Success(result.Data)
				: DatabaseResult<int>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}
	}
}