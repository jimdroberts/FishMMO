using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Entities;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Service for managing character friend relationships in the database.
	/// Provides async operations for CRUD operations on character friend data.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	/// <remarks>
	/// This service manages character friend relationships including:
	/// - Friend relationship creation with atomic INSERT operations
	/// - Friend deletion (individual and bulk)
	/// - Friend retrieval and count queries
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
	public sealed class CharacterFriendService : BaseService<CharacterFriendEntity>, ICharacterFriendService
	{
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
		/// Compiled query for retrieving character friends.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterFriendEntity>>> getFriendsQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterFriends
					.AsNoTracking()
					.Where(f => f.CharacterID == characterId && !f.Deleted)
					.ToList());

		/// <summary>
		/// Compiled query for counting character friends.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<int>> getFriendCountQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterFriends
					.AsNoTracking()
					.Where(f => f.CharacterID == characterId && !f.Deleted)
					.Count());

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterFriendService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterFriendService(INpgsqlDbContextFactory dbContextFactory) : base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveFriendAsync(long characterId, long friendCharacterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0 || friendCharacterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID or friend character ID. Both must be greater than 0.",
					isTransient: false);
			}

			var insertResult = await ExecuteTransactionAsync(async dbContext =>
			{
				var activeCharacterId = await getActiveCharacterIdQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				if (activeCharacterId == 0)
				{
					return;
				}

				var existing = await dbContext.CharacterFriends
					.FirstOrDefaultAsync(f => f.CharacterID == characterId && f.FriendCharacterID == friendCharacterId, cancellationToken)
					.ConfigureAwait(false);
				if (existing != null)
				{
					if (existing.Deleted)
					{
						existing.Deleted = false;
						existing.TimeDeleted = null;
					}
					return;
				}

				var entity = new CharacterFriendEntity
				{
					CharacterID = characterId,
					FriendCharacterID = friendCharacterId,
					TimeCreated = DateTime.UtcNow
				};
				await dbContext.CharacterFriends.AddAsync(entity, cancellationToken).ConfigureAwait(false);
			}).ConfigureAwait(false);

			if (insertResult.IsSuccess)
			{
				return DatabaseResult.Success();
			}

			return insertResult.ErrorCode == "UNIQUE_VIOLATION"
				? DatabaseResult.Success()
				: DatabaseResult.Failure(insertResult.ErrorCode, insertResult.ErrorMessage, insertResult.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteFriendAsync(long characterId, long friendCharacterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0 || friendCharacterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID or friend character ID. Both must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sql = $@"UPDATE {TableName}
					SET deleted = TRUE, time_deleted = {{0}}
					WHERE character_id = {{1}} AND friend_character_id = {{2}} AND deleted = FALSE";
				await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { now, characterId, friendCharacterId }, cancellationToken)
					.ConfigureAwait(false);
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAllFriendsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteTransactionAsync(async dbContext =>
			{
				var now = DateTime.UtcNow;
				var sql = $@"UPDATE {TableName}
					SET deleted = TRUE, time_deleted = {{0}}
					WHERE character_id = {{1}} AND deleted = FALSE";
				await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { now, characterId }, cancellationToken)
					.ConfigureAwait(false);
			}).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterFriendData>>> GetFriendsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterFriendData>>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteReadAsync(async dbContext =>
			{
				var entities = await getFriendsQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false);
				var friends = entities.Select(f => new CharacterFriendData(
					id: f.ID,
					version: f.Version,
					characterID: f.CharacterID,
					friendCharacterID: f.FriendCharacterID
				)).ToList();

				return (IReadOnlyList<CharacterFriendData>)friends;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> GetFriendCountAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult<int>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			return await ExecuteReadAsync(async dbContext =>
				await getFriendCountQuery(dbContext, characterId, cancellationToken).ConfigureAwait(false),
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}