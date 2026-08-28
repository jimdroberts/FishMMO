using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using FishMMO.Database.Npgsql.Entities;
using FishMMO.Database.Npgsql.Services.Interfaces;

namespace FishMMO.Database.Npgsql.Services
{
	/// <summary>
	/// Appends completed currency movements to the economy ledger.
	/// </summary>
	public sealed class CurrencyLedgerService : BaseService<CurrencyLedgerEntity>, ICurrencyLedgerService
	{
		/* Duplicated from FishMMO.Shared.CurrencyMovementState, which this assembly cannot
		 * reference. CurrencyLedgerStateTests pins both sides to the same numbers: renumbering a
		 * member there without changing these would reinterpret every stored row, and a Returned
		 * movement reading back as Absorbed is a refund counted as revenue. */

		/// <summary>Matches FishMMO.Shared.CurrencyMovementState.Absorbed.</summary>
		private const int StateAbsorbed = 1;
		/// <summary>Matches FishMMO.Shared.CurrencyMovementState.Returned.</summary>
		private const int StateReturned = 2;

		public CurrencyLedgerService(INpgsqlDbContextFactory dbContextFactory)
			: base(dbContextFactory)
		{
		}

		/// <inheritdoc />
		public async Task<DatabaseResult> RecordAsync(long characterID, long amount, int reason, int state, CancellationToken cancellationToken = default)
		{
			if (characterID <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Character ID must be greater than zero.");
			}
			if (amount <= 0)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Movement amount must be greater than zero.");
			}

			/* Only a settled outcome may be written. The column defaults to Unsettled, so
			 * accepting an arbitrary state here would let a caller's uninitialised value land as
			 * a row that reports nothing while still counting as a movement. */
			if (state != StateAbsorbed && state != StateReturned)
			{
				return DatabaseResult.Failure(DatabaseErrorCodes.ValidationError, "Movement state must be Absorbed or Returned.");
			}

			return await ExecuteWriteAsync(async dbContext =>
			{
				/* Database server time, as the other append paths use, so rows from different
				 * scene servers order correctly regardless of clock skew between them. */
				string sql = $@"INSERT INTO {TableName} (character_id, amount, reason, state, time_created)
					VALUES ({{0}}, {{1}}, {{2}}, {{3}}, timezone('UTC', CURRENT_TIMESTAMP))";

				await dbContext.Database.ExecuteSqlRawAsync(
					sql,
					new object[] { characterID, amount, reason, state },
					cancellationToken).ConfigureAwait(false);
			}, saveChanges: false, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}
