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
	public sealed class PartyService : IdempotentBaseService<PartyEntity>, IPartyService
	{
		/// <summary>
		/// Compiled query for checking party existence (hot path for party validations).
		/// </summary>
		private static readonly Func<NpgsqlDbContext, long, CancellationToken, Task<bool>> PartyExistsQuery =
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

			return await ExecuteAsync(async (dbContext, ct) =>
			{
				return await PartyExistsQuery(dbContext, partyId, ct).ConfigureAwait(false);
			}, "CheckPartyExists", cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> CreateAsync(long accountId, CancellationToken cancellationToken = default)
		{
			if (accountId <= 0)
			{
				return DatabaseResult<long>.Failure("VALIDATION_ERROR", "Invalid account ID.");
			}

			var requestId = Guid.NewGuid();
			return await ExecuteIdempotentAsync(
				requestId,
				accountId,
				"CreateParty",
				async (dbContext, transaction, ct) =>
			{
				// Use atomic INSERT with RETURNING for proper retry strategy support
				// Optimized: RETURNING only id for better performance
				var result = await dbContext.Parties
					.FromSqlRaw($@"
					INSERT INTO {TableName} (time_created)
					VALUES (CURRENT_TIMESTAMP)
					RETURNING id")
						.AsNoTracking()
						.FirstOrDefaultAsync(ct).ConfigureAwait(false);

				var partyId = result?.ID ?? 0;
				if (partyId <= 0)
				{
					throw new DatabaseQueryException(
						"CreateParty",
						"Failed to create party.",
						"INSERT RETURNING returned no results",
						false,
						null);
				}

				return partyId;
			},
				cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		/// <remarks>
		/// <para><b>Transaction Scope:</b></para>
		/// This operation uses an explicit transaction to ensure atomicity.
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

			// Avoid FK-cascade deadlocks by taking locks in a deterministic order
			// that matches concurrent upsert patterns (dependent rows first, then parent row).
			var transactionResult = await ExecuteTransactionAsync(async (dbContext, transaction, ct) =>
			{
				var characterPartiesTable = dbContext.GetTableName<CharacterPartyEntity>();
				await dbContext.CharacterParties
					.FromSqlRaw($@"SELECT * FROM {characterPartiesTable} WHERE party_id = {{0}} ORDER BY character_id FOR UPDATE", partyId)
					.AsNoTracking()
					.ToListAsync(ct)
					.ConfigureAwait(false);

				var partyUpdatesTable = dbContext.GetTableName<PartyUpdateEntity>();
				await dbContext.PartyUpdates
					.FromSqlRaw($@"SELECT * FROM {partyUpdatesTable} WHERE party_id = {{0}} ORDER BY party_id FOR UPDATE", partyId)
					.AsNoTracking()
					.ToListAsync(ct)
					.ConfigureAwait(false);

				var partyTable = dbContext.GetTableName<PartyEntity>();
				var existingParty = await dbContext.Parties
					.FromSqlRaw($@"SELECT * FROM {partyTable} WHERE id = {{0}} FOR UPDATE", partyId)
					.AsNoTracking()
					.FirstOrDefaultAsync(ct)
					.ConfigureAwait(false);

				// Idempotent: already deleted.
				if (existingParty == null)
					return DatabaseResult.Success();

				await dbContext.Database.ExecuteSqlRawAsync(
					$@"DELETE FROM {partyTable} WHERE id = {{0}}",
					new object[] { partyId },
					ct).ConfigureAwait(false);

				return DatabaseResult.Success();
			}, "DeleteParty", cancellationToken).ConfigureAwait(false);

			return transactionResult.IsSuccess
				? DatabaseResult.Success()
				: DatabaseResult.Failure(transactionResult.ErrorCode, transactionResult.ErrorMessage, transactionResult.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<PartyData>> LoadAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId <= 0)
			{
				return DatabaseResult<PartyData>.Failure("INVALID_PARTY_ID", "Party ID must be greater than zero.");
			}

			return await ExecuteAsync(async (dbContext, ct) =>
			{
				var party = await dbContext.Parties
					.AsNoTracking()
					.FirstOrDefaultAsync(p => p.ID == partyId, ct).ConfigureAwait(false);
				var existingParty = RequireEntityExists(party, "Party", partyId);
				return MapEntityToDto(existingParty);
			}, "LoadParty", cancellationToken).ConfigureAwait(false);
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