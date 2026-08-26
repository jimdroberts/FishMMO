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
	/// <summary>
	/// Records currency holds taken while a transaction completes.
	/// </summary>
	public sealed class CurrencyEscrowService : BaseService<CurrencyEscrowEntity>, ICurrencyEscrowService
	{
		/// <summary>Matches FishMMO.Shared.CurrencyEscrowState.Held.</summary>
		private const int StateHeld = 0;
		/// <summary>Matches FishMMO.Shared.CurrencyEscrowState.Absorbed.</summary>
		private const int StateAbsorbed = 1;
		/// <summary>Matches FishMMO.Shared.CurrencyEscrowState.Returned.</summary>
		private const int StateReturned = 2;

		public CurrencyEscrowService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<long>> HoldAsync(long characterID, long amount, int reason, CancellationToken cancellationToken = default)
		{
			if (characterID <= 0)
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "Character ID must be greater than zero.");
			}
			if (amount <= 0)
			{
				return DatabaseResult<long>.Failure(DatabaseErrorCodes.ValidationError, "Hold amount must be greater than zero.");
			}

			DateTime now = DateTime.UtcNow;

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* RETURNING, so the caller has the row's identity before it touches the balance.
				 * A hold it cannot name is a hold it cannot settle. */
				string sql = $@"INSERT INTO {TableName} (character_id, amount, reason, state, time_created)
					VALUES ({{0}}, {{1}}, {{2}}, {StateHeld}, {{3}})
					RETURNING id";

				return await ExecuteScalarLongAsync(dbContext, sql, new object[] { characterID, amount, reason, now }, cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> AbsorbAsync(long escrowID, CancellationToken cancellationToken = default)
		{
			return await SettleAsync(escrowID, StateAbsorbed, cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<int>> ReturnAsync(long escrowID, CancellationToken cancellationToken = default)
		{
			return await SettleAsync(escrowID, StateReturned, cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Moves a hold out of <see cref="StateHeld"/>.
		/// </summary>
		/// <remarks>
		/// The WHERE clause pins the current state to Held, so settling twice affects no rows
		/// rather than rewriting a settled row. The returned count tells the caller which
		/// happened: 1 means this call settled it, 0 means it was already settled or never
		/// existed, and treating that as success would let a double-return pay out twice.
		/// </remarks>
		private async Task<DatabaseResult<int>> SettleAsync(long escrowID, int state, CancellationToken cancellationToken)
		{
			if (escrowID <= 0)
			{
				return DatabaseResult<int>.Failure(DatabaseErrorCodes.ValidationError, "Escrow ID must be greater than zero.");
			}

			DateTime now = DateTime.UtcNow;

			return await ExecuteWriteAsync(async dbContext =>
			{
				string sql = $@"UPDATE {TableName}
					SET state = {{1}}, time_settled = {{2}}
					WHERE id = {{0}} AND state = {StateHeld}";

				return await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { escrowID, state, now },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<DatabaseResult<List<CurrencyEscrowData>>> FetchHeldAsync(CancellationToken cancellationToken = default)
		{
			return await ExecuteReadAsync(async dbContext =>
			{
				List<CurrencyEscrowEntity> held = await dbContext.CurrencyEscrow
					.AsNoTracking()
					.Where(e => e.State == StateHeld)
					.OrderBy(e => e.ID)
					.ToListAsync(cancellationToken)
					.ConfigureAwait(false);

				List<CurrencyEscrowData> results = new List<CurrencyEscrowData>(held.Count);
				foreach (CurrencyEscrowEntity entity in held)
				{
					results.Add(new CurrencyEscrowData(entity.ID, entity.CharacterID, entity.Amount, entity.Reason));
				}
				return results;
			}, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}
