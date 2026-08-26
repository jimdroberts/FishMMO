using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// A currency hold taken while a transaction completes.
	/// </summary>
	/// <remarks>
	/// One row per hold, settled to Absorbed or Returned. Rows are kept after settlement rather
	/// than deleted, because the table is also the economy ledger: what was spent, on what, and
	/// whether it completed.
	/// </remarks>
	public class CurrencyEscrowEntity
	{
		public long ID { get; set; }

		/// <summary>
		/// The character the currency was taken from, and returned to if the hold is not absorbed.
		/// </summary>
		public long CharacterID { get; set; }

		/// <summary>
		/// Amount held. Always positive.
		/// </summary>
		public long Amount { get; set; }

		/// <summary>
		/// What the hold was taken for. Maps to FishMMO.Shared.CurrencyEscrowReason.
		/// </summary>
		public int Reason { get; set; }

		/// <summary>
		/// Held, Absorbed or Returned. Maps to FishMMO.Shared.CurrencyEscrowState.
		/// </summary>
		public int State { get; set; }

		/// <summary>
		/// When the hold was taken.
		/// </summary>
		public DateTime TimeCreated { get; set; }

		/// <summary>
		/// When the hold was settled, or null while it is still held.
		/// </summary>
		public DateTime? TimeSettled { get; set; }
	}
}
