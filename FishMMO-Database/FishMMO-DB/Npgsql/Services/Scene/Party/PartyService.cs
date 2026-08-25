using System;
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
	/// Service for managing party entities in the database.
	/// Provides async operations for party creation, deletion, and retrieval.
	/// Implements execution strategies for automatic retry on transient database failures.
	/// Returns DatabaseResult for consistent, safe error handling.
	/// </summary>
	/// <remarks>
	/// This service manages party lifecycle including:
	/// - Party creation with generated IDs
	/// - Party deletion with CASCADE cleanup of memberships and update records
	/// - Party existence checks and retrieval
	/// 
	/// Database operations are executed via the BaseService execution wrappers for:
	/// - Automatic transient failure retry
	/// - Centralized exception handling and mapping
	/// - Consistent DatabaseResult pattern
	/// 
	/// Party membership is managed separately by <see cref="CharacterPartyService"/>.
	/// Party updates are tracked by <see cref="PartyUpdateService"/>.
	/// </remarks>
	public sealed class PartyService : BaseService<PartyEntity>, IPartyService
	{
		/// <summary>
		/// Compiled query for checking party existence (hot path for party validations).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<bool>> partyExistsQuery =
			EF.CompileAsyncQuery((NpgsqlDbContext context, long partyId, CancellationToken ct) =>
				context.Parties.Any(p => p.ID == partyId));

		/// <summary>
		/// Initializes a new instance of PartyService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public PartyService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<bool>> ExistsAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId <= 0)
				return DatabaseResult<bool>.Success(false);

			var result = await ExecuteReadAsync(async dbContext =>
				await partyExistsQuery(dbContext, partyId, cancellationToken).ConfigureAwait(false),
				cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> CreateAsync(long worldServerId, CancellationToken cancellationToken = default)
		{
			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var party = new PartyEntity
				{
					WorldServerID = worldServerId,
					TimeCreated = DateTime.UtcNow
				};
				await dbContext.Parties.AddAsync(party, cancellationToken).ConfigureAwait(false);
				return party;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (!result.IsSuccess)
			{
				return DatabaseResult<long>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
			}

			if (result.Data.ID <= 0)
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.DatabaseError, "Failed to create party.", isTransient: true);
			}

			return DatabaseResult<long>.Success(result.Data.ID);
		}

		/// <inheritdoc/>
		/// <remarks>
		/// <para><b>Atomicity:</b></para>
		/// This operation uses a single DELETE statement.
		/// CASCADE delete constraints automatically remove related data:
		/// <list type="bullet">
		/// <item>All character party memberships (character_party table)</item>
		/// <item>Party update notifications (party_update table)</item>
		/// </list>
		/// </remarks>
		public async Task<DatabaseResult> DeleteAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Party ID must be greater than zero.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				// Rely on ON DELETE CASCADE constraints to remove related rows.
				var sql = $@"DELETE FROM {TableName} WHERE id = {{0}}";
				var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { partyId }, cancellationToken)
					.ConfigureAwait(false);

				if (rowsAffected <= 0)
				{
					throw new DatabaseEntityNotFoundException("Party", partyId.ToString());
				}
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<PartyData?>> FetchAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId <= 0)
			{
				return DatabaseResult<PartyData?>.Failure(DatabaseErrorCodes.ValidationError, "Party ID must be greater than zero.");
			}

			var result = await ExecuteReadAsync(async dbContext =>
			{
				var party = await dbContext.Parties
					.AsNoTracking()
					.FirstOrDefaultAsync(p => p.ID == partyId, cancellationToken)
					.ConfigureAwait(false);

				return party != null ? MapEntityToDto(party) : (PartyData?)null;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <summary>
		/// Maps PartyEntity to PartyData DTO.
		/// </summary>
		/// <param name="entity">Party entity from database.</param>
		/// <returns>Party data DTO.</returns>
		private PartyData MapEntityToDto(PartyEntity entity)
		{
			return new PartyData(
				id: entity.ID,
				worldServerID: entity.WorldServerID);
		}
	}
}