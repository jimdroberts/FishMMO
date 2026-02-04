using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Exceptions;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

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
		public async Task<DatabaseResult> PersistAsync(long characterId, long friendCharacterId, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0 || friendCharacterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID or friend character ID. Both must be greater than 0.",
					isTransient: false);
			}

			if (incomingVersion <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid Version. Version must be greater than 0.",
					isTransient: false);
			}

			var insertResult = await ExecuteWriteAsync(async dbContext =>
			{
				var isActiveCharacter = await dbContext.Characters
					.AsNoTracking()
					.AnyAsync(c => c.ID == characterId && !c.Deleted, cancellationToken)
					.ConfigureAwait(false);

				if (!isActiveCharacter)
				{
					throw new DatabaseEntityNotFoundException("Character", characterId.ToString(), "Character not found or deleted.");
				}

				var now = DateTime.UtcNow;
				var sql = $@"
					INSERT INTO {TableName}
						(character_id, friend_character_id, version, time_created, deleted, time_deleted)
					VALUES
						({{0}}, {{1}}, {{2}}, {{3}}, FALSE, NULL)
					ON CONFLICT (character_id, friend_character_id)
					DO UPDATE SET
						deleted = FALSE,
						time_deleted = NULL,
						version = EXCLUDED.version
					WHERE
						EXCLUDED.version > {TableName}.version";

				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { characterId, friendCharacterId, incomingVersion, now },
					cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var existing = await dbContext.CharacterFriends
						.AsNoTracking()
						.Where(f => f.CharacterID == characterId && f.FriendCharacterID == friendCharacterId)
						.Select(f => new { f.Version })
						.FirstOrDefaultAsync(cancellationToken)
						.ConfigureAwait(false);

					if (existing != null)
					{
						if (existing.Version == incomingVersion)
						{
							throw new DuplicateReplayException("Friend persist rejected because the Version was a duplicate replay.");
						}

						throw new StaleStateException("Friend persist rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return insertResult;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long characterId, long friendCharacterId, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0 || friendCharacterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID or friend character ID. Both must be greater than 0.",
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
				var now = DateTime.UtcNow;
				var sql = $@"UPDATE {TableName}
					SET deleted = TRUE, time_deleted = {{0}}, version = {{1}}
					WHERE character_id = {{2}} AND friend_character_id = {{3}} AND deleted = FALSE AND version < {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { now, incomingVersion, characterId, friendCharacterId }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var stillActive = await dbContext.CharacterFriends
						.AsNoTracking()
						.AnyAsync(f => f.CharacterID == characterId && f.FriendCharacterID == friendCharacterId && !f.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (stillActive)
					{
						throw new StaleStateException("Friend delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeleteAsync(long characterId, long incomingVersion, CancellationToken cancellationToken = default)
		{
			if (characterId <= 0)
			{
				return DatabaseResult.Failure(
					"VALIDATION_ERROR",
					"Invalid character ID. Character ID must be greater than 0.",
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
				var now = DateTime.UtcNow;
				var sql = $@"UPDATE {TableName}
					SET deleted = TRUE, time_deleted = {{0}}, version = {{1}}
					WHERE character_id = {{2}} AND deleted = FALSE AND version < {{1}}";
				var rowsAffected = await dbContext.Database
					.ExecuteSqlRawAsync(sql, new object[] { now, incomingVersion, characterId }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected == 0)
				{
					var anyActive = await dbContext.CharacterFriends
						.AsNoTracking()
						.AnyAsync(f => f.CharacterID == characterId && !f.Deleted, cancellationToken)
						.ConfigureAwait(false);

					if (anyActive)
					{
						throw new StaleStateException("Friend delete rejected due to a stale Version.");
					}
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterFriendData>>> FetchAsync(long characterId, CancellationToken cancellationToken = default)
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
		public async Task<DatabaseResult<int>> CountAsync(long characterId, CancellationToken cancellationToken = default)
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