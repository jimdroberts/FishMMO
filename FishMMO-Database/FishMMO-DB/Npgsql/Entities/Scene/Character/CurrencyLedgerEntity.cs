using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One completed currency movement.
	/// </summary>
	/// <remarks>
	/// Written once, after the movement it describes has already happened and its balance change
	/// has been persisted. Rows are never updated and never deleted: the table is the economy
	/// ledger — what was spent, on what, and whether the transaction completed.
	/// </remarks>
	public class CurrencyLedgerEntity
	{
		public long ID { get; set; }

		/// <summary>
		/// The character whose balance moved.
		/// </summary>
		public long CharacterID { get; set; }

		/// <summary>
		/// Amount moved. Always positive; <see cref="State"/> says which direction it went.
		/// </summary>
		public long Amount { get; set; }

		/// <summary>
		/// What the movement was for. Maps to FishMMO.Shared.CurrencyMovementReason.
		/// </summary>
		public int Reason { get; set; }

		/// <summary>
		/// Absorbed or Returned. Maps to FishMMO.Shared.CurrencyMovementState.
		/// </summary>
		/// <remarks>
		/// Set at insert, because the outcome is already known by then. A row left at the column
		/// default (Unsettled) is an insert that omitted it, not a transaction still in flight.
		/// </remarks>
		public int State { get; set; }

		/// <summary>
		/// When the movement was recorded.
		/// </summary>
		public DateTime TimeCreated { get; set; }
	}
}
