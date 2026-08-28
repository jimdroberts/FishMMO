using System.Threading;
using System.Threading.Tasks;

namespace FishMMO.Database.Npgsql.Services.Interfaces
{
	/// <summary>
	/// Records completed currency movements.
	/// </summary>
	/// <remarks>
	/// Append-only. A movement is recorded after its balance change has been persisted and its
	/// outcome is known, so there is nothing to settle afterwards and no state for a later pass to
	/// interpret. A lost row is a gap in reporting, never a gap in the economy.
	/// </remarks>
	public interface ICurrencyLedgerService
	{
		/// <summary>
		/// Appends one completed movement.
		/// </summary>
		/// <param name="characterID">The character whose balance moved.</param>
		/// <param name="amount">Amount moved. Must be positive.</param>
		/// <param name="reason">Maps to FishMMO.Shared.CurrencyMovementReason.</param>
		/// <param name="state">Maps to FishMMO.Shared.CurrencyMovementState. Absorbed or Returned.</param>
		Task<DatabaseResult> RecordAsync(long characterID, long amount, int reason, int state, CancellationToken cancellationToken = default);
	}
}
