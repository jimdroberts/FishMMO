using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One thing held in a character's house vault after their land was taken back.
	/// </summary>
	/// <remarks>
	/// Reclaiming a plot destroys a house somebody built and paid for, and doing that with no way
	/// back would make a missed tax payment the most punishing event in the game. The vault is the
	/// answer: what stood on the plot is moved here rather than deleted, and the owner may buy it
	/// back or let it go.
	///
	/// <para>Keyed by character, not by plot. The plot is about to belong to somebody else — that is
	/// the whole reason these rows exist — so anything hanging off it would be cleared out from
	/// under them by the next owner's first change. <see cref="OriginalPlotID"/> is kept as a label,
	/// deliberately without a foreign key, so the row survives the plot being reused.</para>
	/// </remarks>
	public class PlotVaultEntity : IVersionedEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>
		/// Application-level concurrency token. Incremented on every write to detect stale updates.
		/// </summary>
		public long Version { get; set; }

		/// <summary>The character whose vault this is.</summary>
		public long CharacterID { get; set; }

		/// <summary>
		/// The structure template that was stored.
		/// </summary>
		/// <remarks>
		/// A template rather than a structure row. The structure itself is deleted with the plot it
		/// stood on, and its position was relative to a foundation that is no longer the owner's, so
		/// there is nothing about the placement worth keeping. What the player is owed back is the
		/// thing, not where it used to stand.
		/// </remarks>
		public int TemplateID { get; set; }

		/// <summary>How many of it are held.</summary>
		/// <remarks>
		/// Stacked, so a house with forty identical fence panels is one row rather than forty. The
		/// retrieval fee is charged per row, which makes the stack the unit a player buys back.
		/// </remarks>
		public int Amount { get; set; }

		/// <summary>
		/// The plot it came off, kept as a label rather than a relation.
		/// </summary>
		/// <remarks>
		/// No foreign key on purpose. The plot is being handed to somebody else and may be claimed,
		/// built on and reclaimed again many times over; a cascading key would delete the previous
		/// owner's belongings the moment the new one's house was cleared.
		/// </remarks>
		public long OriginalPlotID { get; set; }

		/// <summary>
		/// When it went into the vault (UTC). The instant the retrieval fee is measured from.
		/// </summary>
		public DateTime StoredAtUtc { get; set; }

		/// <summary>
		/// The fee charged the moment it was stored, before any time had passed.
		/// </summary>
		/// <remarks>
		/// Frozen onto the row rather than recomputed from the template's price. A server that
		/// rebalances what a structure costs must not thereby change what a player owes on
		/// something already sitting in their vault — they were quoted a figure when it went in.
		/// </remarks>
		public long BaseFee { get; set; }

		/// <summary>
		/// How much of <see cref="BaseFee"/> is added per day stored.
		/// </summary>
		/// <remarks>
		/// Frozen for the same reason as the base fee. Together they make the row self-describing:
		/// what it costs to get back is answerable from the row and the clock, with nothing else
		/// loaded.
		/// </remarks>
		public float FeeRatePerDay { get; set; }
	}
}
