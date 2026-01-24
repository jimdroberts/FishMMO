using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <inheritdoc/>
	public sealed class PartyUpdateService : BaseService<PartyUpdateEntity>, IPartyUpdateService
	{
		/// <summary>
		/// Initializes a new instance of PartyUpdateService.
		/// </summary>
		/// <param name="dbContextFactory">DbContext factory for creating contexts.</param>
		/// <exception cref="ArgumentNullException">Thrown when dbContextFactory is null.</exception>
		public PartyUpdateService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult> SaveAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId <= 0)
			{
				return DatabaseResult.Failure("INVALID_PARTY_ID", "Party ID must be greater than zero.");
			}

			var result = await ExecuteRawSqlAsync(
				$@"INSERT INTO {TableName} (party_id, time_created, last_update) 
					VALUES ({{0}}, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP) 
					ON CONFLICT (party_id) 
					DO UPDATE SET last_update = GREATEST({TableName}.last_update, EXCLUDED.last_update)",
				"SavePartyUpdate",
				new object[] { partyId },
				entityName: "PartyUpdate",
				entityId: partyId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken).ConfigureAwait(false);

			return result.IsSuccess ? DatabaseResult.Success() : DatabaseResult.Failure(result.ErrorCode, result.ErrorMessage, result.IsTransient);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId <= 0)
			{
				return DatabaseResult<int>.Failure("INVALID_PARTY_ID", "Party ID must be greater than zero.");
			}

			return await ExecuteRawSqlAsync(
				$"DELETE FROM {TableName} WHERE party_id = {{0}}",
				"DeletePartyUpdate",
				new object[] { partyId },
				entityName: "PartyUpdate",
				entityId: partyId,
				requireRowsAffected: false,
				cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<List<PartyUpdateData>>> FetchAsync(
			List<long> partyIds,
			DateTime lastFetch,
			CancellationToken cancellationToken = default)
		{
			if (partyIds == null || partyIds.Count == 0)
				return DatabaseResult<List<PartyUpdateData>>.Success(new List<PartyUpdateData>());

			return await ExecuteAsync(async (dbContext, ct) =>
			{
				var updates = await dbContext.PartyUpdates
					.AsNoTracking()
					.Where(u => u.LastUpdate >= lastFetch && partyIds.Contains(u.PartyID))
					.ToListAsync(ct).ConfigureAwait(false);

				return updates.Select(MapEntityToDto).ToList();
			}, "FetchPartyUpdates", cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Maps PartyUpdateEntity to PartyUpdateData DTO.
		/// </summary>
		/// <param name="entity">Party update entity from database.</param>
		/// <returns>Party update data DTO.</returns>
		private PartyUpdateData MapEntityToDto(PartyUpdateEntity entity)
		{
			return new PartyUpdateData(
				id: entity.ID,
				partyID: entity.PartyID,
				lastUpdate: entity.LastUpdate);
		}
	}
}