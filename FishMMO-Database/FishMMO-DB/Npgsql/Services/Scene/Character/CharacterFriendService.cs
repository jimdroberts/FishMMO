using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;
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
	public sealed class CharacterFriendService : ICharacterFriendService
	{
		/// <summary>
		/// Factory for creating database contexts.
		/// </summary>
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterFriendService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterFriendService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveFriendAsync(long characterId, long friendCharacterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0 || friendCharacterId == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID or friend character ID. Both must be greater than 0.",
					isTransient: false);
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					// Use atomic INSERT with ON CONFLICT DO NOTHING for thread safety
					var tableName = dbContext.GetTableName<CharacterFriendEntity>();
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"INSERT INTO {tableName} (character_id, friend_character_id)
						   VALUES ({characterId}, {friendCharacterId})
						   ON CONFLICT (character_id, friend_character_id) DO NOTHING",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("SaveFriend", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_friend_character_id_fkey",
						"Character does not exist.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SaveFriend",
						"Failed to save friend relationship due to a database error.",
						$"DbUpdateException in SaveFriendAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SaveFriend",
						"An unexpected error occurred while saving friend relationship.",
						$"Unexpected error in SaveFriendAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteFriendAsync(long characterId, long friendCharacterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0 || friendCharacterId == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID or friend character ID. Both must be greater than 0.",
					isTransient: false);
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					// Use atomic DELETE for thread safety
					var tableName = dbContext.GetTableName<CharacterFriendEntity>();
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"DELETE FROM {tableName} 
						   WHERE character_id = {characterId} AND friend_character_id = {friendCharacterId}",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("DeleteFriend", 10));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteFriend",
						"Failed to delete friend relationship due to a database error.",
						$"DbUpdateException in DeleteFriendAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteFriend",
						"An unexpected error occurred while deleting friend relationship.",
						$"Unexpected error in DeleteFriendAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAllFriendsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					// Use atomic DELETE for thread safety
					var tableName = dbContext.GetTableName<CharacterFriendEntity>();
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"DELETE FROM {tableName} WHERE character_id = {characterId}",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("DeleteAllFriends", 10));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteAllFriends",
						"Failed to delete all friends due to a database error.",
						$"DbUpdateException in DeleteAllFriendsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeleteAllFriends",
						"An unexpected error occurred while deleting all friends.",
						$"Unexpected error in DeleteAllFriendsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterFriendData>>> GetFriendsAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterFriendData>>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var friends = await dbContext.CharacterFriends
					.AsNoTracking()
					.Where(f => f.CharacterID == characterId)
					.Select(f => new CharacterFriendData
					{
						ID = f.ID,
						CharacterID = f.CharacterID,
						FriendCharacterID = f.FriendCharacterID
					})
					.ToListAsync(cancellationToken);

				return DatabaseResult<IReadOnlyList<CharacterFriendData>>.Success(friends);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<IReadOnlyList<CharacterFriendData>>.FromException(
					new DatabaseTimeoutException("GetFriends", 10));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterFriendData>>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterFriendData>>.FromException(
					new DatabaseQueryException(
						"GetFriends",
						"An unexpected error occurred while retrieving friends.",
						$"Unexpected error in GetFriendsAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> GetFriendCountAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<int>.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
					isTransient: false);
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var count = await dbContext.CharacterFriends
					.AsNoTracking()
					.Where(f => f.CharacterID == characterId)
					.CountAsync(cancellationToken);

				return DatabaseResult<int>.Success(count);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseTimeoutException("GetFriendCount", 10));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseQueryException(
						"GetFriendCount",
						"An unexpected error occurred while retrieving friend count.",
						$"Unexpected error in GetFriendCountAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}
	}
}