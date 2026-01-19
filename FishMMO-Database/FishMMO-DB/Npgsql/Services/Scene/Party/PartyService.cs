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

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				return await PartyExistsQuery(dbContext, partyId, cancellationToken);
			}, "CheckPartyExists", cancellationToken);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<long>> CreateAsync(CancellationToken cancellationToken = default)
		{
			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				// Use atomic INSERT with RETURNING for proper retry strategy support
				// Optimized: RETURNING only id for better performance
				var result = await dbContext.Parties
					.FromSqlInterpolated($@"
					INSERT INTO {TableName} (time_created)
					VALUES (CURRENT_TIMESTAMP)
					RETURNING id")
						.AsNoTracking()
						.FirstOrDefaultAsync(cancellationToken);

				return result?.ID ?? 0;
			}, "CreateParty", cancellationToken);
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

		// Use explicit transaction for atomic multi-table operation
		return await ExecuteInTransactionAsync(async (dbContext, transaction) =>
		{
			var rowsAffected = await dbContext.Database.ExecuteSqlInterpolatedAsync(
				$"DELETE FROM {TableName} WHERE id = {partyId}",
				cancellationToken);

			if (rowsAffected == 0)
			{
				throw new DatabaseEntityNotFoundException(
					"Party",
					partyId.ToString(),
					"Party not found.");
			}
		}, "DeleteParty", cancellationToken);
	}

		/// <inheritdoc/>
		public async Task<DatabaseResult<PartyData>> LoadAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId <= 0)
			{
				return DatabaseResult<PartyData>.Failure("INVALID_PARTY_ID", "Party ID must be greater than zero.");
			}

			return await ExecuteWithStrategyAsync(async dbContext =>
			{
				var party = await dbContext.Parties
					.AsNoTracking()
					.FirstOrDefaultAsync(p => p.ID == partyId, cancellationToken);

				ValidateEntityExists(party, "Party", partyId.ToString());

				return MapEntityToDto(party!);
			}, "LoadParty", cancellationToken);
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