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
		public async Task<DatabaseResult> PersistAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId <= 0)
			{
				return DatabaseResult.Failure("INVALID_PARTY_ID", "Party ID must be greater than zero.");
			}

			var now = DateTime.UtcNow;
			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"INSERT INTO {TableName} (party_id, time_created, last_update)
					VALUES ({{0}}, {{1}}, {{1}})
					ON CONFLICT (party_id) DO UPDATE
					SET last_update = EXCLUDED.last_update
					WHERE last_update < EXCLUDED.last_update";

				await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { partyId, now },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<int>> DeleteAsync(long partyId, CancellationToken cancellationToken = default)
		{
			if (partyId <= 0)
			{
				return DatabaseResult<int>.Failure("INVALID_PARTY_ID", "Party ID must be greater than zero.");
			}

			var result = await ExecuteWriteAsync(async dbContext =>
			{
				var sql = $@"DELETE FROM {TableName} WHERE party_id = {{0}}";
				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { partyId },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
		}

		/// <inheritdoc/>
		public async Task<DatabaseResult<List<PartyUpdateData>>> FetchAsync(
			List<long> partyIds,
			DateTime lastFetch,
			CancellationToken cancellationToken = default)
		{
			if (partyIds == null || partyIds.Count == 0)
				return DatabaseResult<List<PartyUpdateData>>.Success(new List<PartyUpdateData>());

			var result = await ExecuteReadAsync(async dbContext =>
			{
				var partyIdArray = partyIds.Distinct().ToArray();
				var sql = $@"SELECT * FROM {TableName}
					WHERE last_update >= {{0}}
					AND party_id = ANY({{1}})";

				var updates = await dbContext.PartyUpdates
					.FromSqlRaw(sql, lastFetch, partyIdArray)
					.AsNoTracking()
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				return updates.Select(MapEntityToDto).ToList();
			}, cancellationToken: cancellationToken).ConfigureAwait(false);

			return result;
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