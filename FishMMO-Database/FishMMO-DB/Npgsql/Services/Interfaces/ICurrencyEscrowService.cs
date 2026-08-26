using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database.Data;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Records currency holds taken while a transaction completes.
	/// </summary>
	/// <remarks>
	/// The row is what makes an interrupted transaction recoverable. Deducting a balance and
	/// granting something in return has a window between the two halves, and without a record of
	/// that window a crash inside it leaves the deduction persisted with nothing to say a
	/// transaction was ever in flight.
	/// </remarks>
	public interface ICurrencyEscrowService
	{
		/// <summary>
		/// Records a hold and returns its ID.
		/// </summary>
		/// <remarks>
		/// Written before the balance is touched, so the ordering favours a hold with no matching
		/// deduction over a deduction with no hold. The first is a returnable orphan; the second
		/// is money that is simply gone.
		/// </remarks>
		Task<DatabaseResult<long>> HoldAsync(long characterID, long amount, int reason, CancellationToken cancellationToken = default);

		/// <summary>
		/// Settles a hold in favour of the transaction. The currency leaves the economy.
		/// </summary>
		Task<DatabaseResult<int>> AbsorbAsync(long escrowID, CancellationToken cancellationToken = default);

		/// <summary>
		/// Settles a hold back to the character.
		/// </summary>
		Task<DatabaseResult<int>> ReturnAsync(long escrowID, CancellationToken cancellationToken = default);

		/// <summary>
		/// Every hold still unsettled, for reconciliation at startup.
		/// </summary>
		/// <remarks>
		/// Anything this returns is an interrupted transaction: the process that took it is gone,
		/// so nothing is going to settle it. The caller returns them to their owners.
		/// </remarks>
		Task<DatabaseResult<List<CurrencyEscrowData>>> FetchHeldAsync(CancellationToken cancellationToken = default);
	}
}
