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
	/// - OperationCanceledException → DatabaseTimeoutException
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
		/// Compiled query for retrieving character friends.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<List<CharacterFriendEntity>>> GetFriendsQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterFriends
					.AsNoTracking()
					.Where(f => f.CharacterID == characterId)
					.ToList());

		/// <summary>
		/// Compiled query for counting character friends.
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<int>> GetFriendCountQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long characterId, CancellationToken ct) =>
				context.CharacterFriends
					.AsNoTracking()
					.Where(f => f.CharacterID == characterId)
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
			if (characterId == 0 || friendCharacterId == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID or friend character ID. Both must be greater than 0.");
			}

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				// Use atomic INSERT with ON CONFLICT DO NOTHING for thread safety
				await dbContext.Database.ExecuteSqlInterpolatedAsync(
					$@"INSERT INTO {TableName} (character_id, friend_character_id)
				   VALUES ({characterId}, {friendCharacterId})
				   ON CONFLICT (character_id, friend_character_id) DO NOTHING",
					cancellationToken);
			}, "SaveFriend", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteFriendAsync(long characterId, long friendCharacterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0 || friendCharacterId == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID or friend character ID. Both must be greater than 0.");
			}

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				// Use atomic DELETE for thread safety
				await dbContext.Database.ExecuteSqlInterpolatedAsync(
					$@"DELETE FROM {TableName} 
					   WHERE character_id = {characterId} AND friend_character_id = {friendCharacterId}",
					cancellationToken);
			},
			"DeleteFriend",
			cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAllFriendsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.");
			}

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				// Use atomic DELETE for thread safety
				await dbContext.Database.ExecuteSqlInterpolatedAsync(
					$@"DELETE FROM {TableName} WHERE character_id = {characterId}",
					cancellationToken);
			},
			"DeleteAllFriends",
			cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterFriendData>>> GetFriendsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterFriendData>>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.");
			}

			return await ExecuteWithStrategyAsync(
				async (dbContext) =>
				{
					var entities = await GetFriendsQuery(dbContext, characterId, cancellationToken);
					var friends = entities.Select(f => new CharacterFriendData(
						id: f.ID,
						characterID: f.CharacterID,
						friendCharacterID: f.FriendCharacterID
					)).ToList();

					return (IReadOnlyList<CharacterFriendData>)friends;
				},
				"GetFriends",
				cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> GetFriendCountAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<int>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.");
			}

			return await ExecuteWithStrategyAsync(
				async (dbContext) =>
				{
					return await GetFriendCountQuery(dbContext, characterId, cancellationToken);
				},
				"GetFriendCount",
				cancellationToken);
		}
	}
}