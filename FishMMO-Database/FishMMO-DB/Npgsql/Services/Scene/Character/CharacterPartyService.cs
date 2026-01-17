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
	/// All database exceptions are caught and wrapped in appropriate DatabaseException types:
	/// - OperationCanceledException → DatabaseTimeoutException
	/// - PostgresException (23505) → DatabaseConstraintException (Unique violation)
	/// - PostgresException (23503) → DatabaseConstraintException (Foreign key violation)
	/// - NpgsqlException → DatabaseConnectionException
	/// - DbUpdateException → DatabaseQueryException
	/// - Exception → DatabaseQueryException
	/// 
	/// Methods return DatabaseResult to provide structured error handling
	/// without throwing exceptions to calling code.
	/// </remarks>
	public sealed class CharacterPartyService : ICharacterPartyService
	{
		/// <summary>
		/// Factory for creating database contexts.
		/// </summary>
		private readonly INpgsqlDbContextFactory dbContextFactory;

		/// <summary>
		/// Initializes a new instance of the <see cref="CharacterPartyService"/> class.
		/// </summary>
		/// <param name="dbContextFactory">Factory for creating database contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public CharacterPartyService(INpgsqlDbContextFactory dbContextFactory)
		{
			this.dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SavePartyMembershipAsync(CharacterPartyData partyData, CancellationToken cancellationToken = default)
		{
			if (partyData.CharacterID == 0 || partyData.PartyID == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character or party ID");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterPartyEntity>();

					// Use PostgreSQL UPSERT for atomic insert-or-update
					return await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"INSERT INTO {tableName} 
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
					return DatabaseResult.Failure("SAVE_FAILED", "No rows affected");
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("SavePartyMembership", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_parties_character_id_key",
						"Character is already in a party.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_parties_character_id_fkey",
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
						"SavePartyMembership",
						"Failed to save party membership due to a database error.",
						$"DbUpdateException in SavePartyMembershipAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"SavePartyMembership",
						"An unexpected error occurred while saving party membership.",
						$"Unexpected error in SavePartyMembershipAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> DeletePartyMembershipAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterPartyEntity>();

					// Use atomic DELETE for thread safety
					await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"DELETE FROM {tableName} WHERE character_id = {characterId}",
						cancellationToken);
				});

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("DeletePartyMembership", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_parties_constraint",
						"Constraint violation while deleting party membership.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_parties_constraint",
						"Cannot delete party membership due to foreign key constraint.",
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
						"DeletePartyMembership",
						"Failed to delete party membership due to a database error.",
						$"DbUpdateException in DeletePartyMembershipAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"DeletePartyMembership",
						"An unexpected error occurred while deleting party membership.",
						$"Unexpected error in DeletePartyMembershipAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> UpdateRankAsync(long characterId, long partyId, byte rank, CancellationToken cancellationToken = default)
		{
			if (characterId == 0 || partyId == 0)
			{
				return DatabaseResult.Failure("VALIDATION_ERROR", "Invalid character or party ID");
			}

			await using var dbContext = dbContextFactory.CreateDbContext();

			try
			{
				var strategy = dbContext.Database.CreateExecutionStrategy();

				var rowsAffected = await strategy.ExecuteAsync(async () =>
				{
					var tableName = dbContext.GetTableName<CharacterPartyEntity>();

					// Atomic update without loading entity
					return await dbContext.Database.ExecuteSqlInterpolatedAsync(
						$@"UPDATE {tableName} 
						SET rank = {rank} 
						WHERE character_id = {characterId} AND party_id = {partyId}",
						cancellationToken);
				});

				if (rowsAffected == 0)
				{
					return DatabaseResult.FromException(
						new DatabaseEntityNotFoundException(
							"PartyMembership",
							$"character_id={characterId}, party_id={partyId}",
							"Membership not found"));
				}

				return DatabaseResult.Success();
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult.FromException(
					new DatabaseTimeoutException("UpdateRank", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_parties_constraint",
						"Constraint violation while updating rank.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_parties_constraint",
						"Cannot update rank due to foreign key constraint.",
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
						"UpdateRank",
						"Failed to update rank due to a database error.",
						$"DbUpdateException in UpdateRankAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult.FromException(
					new DatabaseQueryException(
						"UpdateRank",
						"An unexpected error occurred while updating rank.",
						$"Unexpected error in UpdateRankAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<CharacterPartyData?>> GetPartyMembershipAsync(long characterId, CancellationToken cancellationToken = default)
		{
			if (characterId == 0)
			{
				return DatabaseResult<CharacterPartyData?>.Failure("VALIDATION_ERROR", "Invalid character ID");
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var membership = await dbContext.CharacterParties
					.AsNoTracking()
					.Where(p => p.CharacterID == characterId)
					.Select(p => new CharacterPartyData
					{
						ID = p.ID,
						CharacterID = p.CharacterID,
						PartyID = p.PartyID,
						Rank = p.Rank,
						HealthPCT = p.HealthPCT
					})
					.FirstOrDefaultAsync(cancellationToken);

				return DatabaseResult<CharacterPartyData?>.Success(membership);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<CharacterPartyData?>.FromException(
					new DatabaseTimeoutException("GetPartyMembership", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult<CharacterPartyData?>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_parties_constraint",
						"Constraint violation while retrieving party membership.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<CharacterPartyData?>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_parties_constraint",
						"Foreign key constraint issue while retrieving party membership.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<CharacterPartyData?>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<CharacterPartyData?>.FromException(
					new DatabaseQueryException(
						"GetPartyMembership",
						"Failed to retrieve party membership due to a database error.",
						$"DbUpdateException in GetPartyMembershipAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<CharacterPartyData?>.FromException(
					new DatabaseQueryException(
						"GetPartyMembership",
						"An unexpected error occurred while retrieving party membership.",
						$"Unexpected error in GetPartyMembershipAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}
		/// <inheritdoc/>
		public async Task<DatabaseResult<IReadOnlyList<CharacterPartyData>>> GetPartyMembersAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId == 0)
			{
				return DatabaseResult<IReadOnlyList<CharacterPartyData>>.Failure("VALIDATION_ERROR", "Invalid party ID");
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var members = await dbContext.CharacterParties
					.AsNoTracking()
					.Where(p => p.PartyID == partyId)
					.Select(p => new CharacterPartyData
					{
						ID = p.ID,
						CharacterID = p.CharacterID,
						PartyID = p.PartyID,
						Rank = p.Rank,
						HealthPCT = p.HealthPCT
					})
					.ToListAsync(cancellationToken);

				return DatabaseResult<IReadOnlyList<CharacterPartyData>>.Success(members);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<IReadOnlyList<CharacterPartyData>>.FromException(
					new DatabaseTimeoutException("GetPartyMembers", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult<IReadOnlyList<CharacterPartyData>>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_parties_constraint",
						"Constraint violation while retrieving party members.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<IReadOnlyList<CharacterPartyData>>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_parties_constraint",
						"Foreign key constraint issue while retrieving party members.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterPartyData>>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterPartyData>>.FromException(
					new DatabaseQueryException(
						"GetPartyMembers",
						"Failed to retrieve party members due to a database error.",
						$"DbUpdateException in GetPartyMembersAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<IReadOnlyList<CharacterPartyData>>.FromException(
					new DatabaseQueryException(
						"GetPartyMembers",
						"An unexpected error occurred while retrieving party members.",
						$"Unexpected error in GetPartyMembersAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> GetPartyMemberCountAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId == 0)
			{
				return DatabaseResult<int>.Failure("VALIDATION_ERROR", "Invalid party ID");
			}

			try
			{
				await using var dbContext = dbContextFactory.CreateDbContext();

				var count = await dbContext.CharacterParties
					.AsNoTracking()
					.Where(p => p.PartyID == partyId)
					.CountAsync(cancellationToken);

				return DatabaseResult<int>.Success(count);
			}
			catch (OperationCanceledException)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseTimeoutException("GetPartyMemberCount", 10));
			}
			catch (PostgresException ex) when (ex.SqlState == "23505") // Unique violation
			{
				return DatabaseResult<int>.FromException(
					new DatabaseConstraintException(
						ConstraintType.Unique,
						"character_parties_constraint",
						"Constraint violation while counting party members.",
						ex));
			}
			catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation
			{
				return DatabaseResult<int>.FromException(
					new DatabaseConstraintException(
						ConstraintType.ForeignKey,
						"character_parties_constraint",
						"Foreign key constraint issue while counting party members.",
						ex));
			}
			catch (NpgsqlException ex)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseConnectionException("database", ex));
			}
			catch (DbUpdateException ex)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseQueryException(
						"GetPartyMemberCount",
						"Failed to count party members due to a database error.",
						$"DbUpdateException in GetPartyMemberCountAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
			catch (Exception ex)
			{
				return DatabaseResult<int>.FromException(
					new DatabaseQueryException(
						"GetPartyMemberCount",
						"An unexpected error occurred while counting party members.",
						$"Unexpected error in GetPartyMemberCountAsync: {ex.Message}",
						isTransient: false,
						innerException: ex));
			}
		}
	}
}