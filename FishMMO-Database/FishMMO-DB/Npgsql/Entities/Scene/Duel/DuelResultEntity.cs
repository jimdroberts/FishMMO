using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One finished duel: who won, who lost, and what it did to their ratings (the Duels module).
	/// </summary>
	/// <remarks>
	/// The history, and the write's idempotency: the request key is unique, so a result retried after its commit reply
	/// was lost finds its own row and is not counted twice.
	/// </remarks>
	public class DuelResultEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }
		/// <summary>Chosen by the scene server once per duel; unique.</summary>
		public Guid RequestKey { get; set; }
		/// <summary>The winner, or for a draw the first side.</summary>
		public long WinnerID { get; set; }
		/// <summary>The loser, or for a draw the second side.</summary>
		public long LoserID { get; set; }
		/// <summary>True when the duel ran out of time.</summary>
		public bool Draw { get; set; }
		/// <summary>How the duel ended (the game's own reason code).</summary>
		public short Reason { get; set; }
		/// <summary>The winner's rating change.</summary>
		public int WinnerRatingChange { get; set; }
		/// <summary>The loser's rating change.</summary>
		public int LoserRatingChange { get; set; }
		/// <summary>When it was recorded (UTC).</summary>
		public DateTime CreatedUtc { get; set; }
	}
}
