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
	/// <inheritdoc/>
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

			return result.IsSuccess
				? DatabaseResult<bool>.Success(result.Data)
				: DatabaseResult<bool>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> CreateAsync(long accountId, CancellationToken cancellationToken = default)
		{
			if (accountId <= 0)
			{
				return DatabaseResult<long>.Failure("VALIDATION_ERROR", "Invalid account ID.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var party = new PartyEntity
				{
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
				return DatabaseResult<long>.Failure("DATABASE_ERROR", "Failed to create party.", isTransient: true);
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
				return DatabaseResult.Failure("INVALID_PARTY_ID", "Party ID must be greater than zero.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				// Rely on ON DELETE CASCADE constraints to remove related rows.
				var sql = $@"DELETE FROM {TableName} WHERE id = {{0}}";
				await dbContext.Database.ExecuteSqlRawAsync(sql, new object[] { partyId }, cancellationToken)
					.ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<PartyData>> LoadAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId <= 0)
			{
				return DatabaseResult<PartyData>.Failure("INVALID_PARTY_ID", "Party ID must be greater than zero.");
			}

			var result = await ExecuteReadAsync(async dbContext =>
			{
				var party = await dbContext.Parties
					.AsNoTracking()
					.FirstOrDefaultAsync(p => p.ID == partyId, cancellationToken)
					.ConfigureAwait(false);
				if (party == null)
				{
					throw new DatabaseEntityNotFoundException("Party", partyId.ToString());
				}
				return MapEntityToDto(party);
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess
				? DatabaseResult<PartyData>.Success(result.Data)
				: DatabaseResult<PartyData>.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <summary>
		/// Maps PartyEntity to PartyData DTO.
		/// </summary>
		/// <param name="entity">Party entity from database.</param>
		/// <returns>Party data DTO.</returns>
		private PartyData MapEntityToDto(PartyEntity entity)
		{
			return new PartyData(
				id: entity.ID);
		}
	}
}