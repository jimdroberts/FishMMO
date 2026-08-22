using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Database entity holding one character's "already chosen" bitmask for one dialogue template.
	/// </summary>
	/// <remarks>
	/// <para>Deliberately carries no <c>Version</c> column, unlike the character tables beside it.
	/// The optimistic-concurrency machinery exists to decide which of two competing writes to the
	/// same row wins, and for this row the answer is always "both": the mask only ever gains bits,
	/// so the write merges with <c>choices | EXCLUDED.choices</c> rather than overwriting. That is
	/// idempotent under the execution strategy's retries and safe when a character's old and new
	/// scene servers overlap during a transfer — and, critically, it can never lose a bit, which is
	/// the only direction that matters: a lost bit is a one-time dialogue reward the player can
	/// take again.</para>
	///
	/// <para>Also carries no soft-delete columns. There is nothing to tombstone — a mask is either
	/// there or the character has taken no tracked choice in that template, which reads the same.
	/// Clearing one is an authoring operation (see <c>ResetTemplateAsync</c>) and is a real delete.</para>
	/// </remarks>
	public class CharacterDialogueChoiceEntity
	{
		/// <summary>
		/// Foreign key to the owning character. Part of the composite primary key.
		/// </summary>
		public long CharacterID { get; set; }

		/// <summary>Navigation to the owning character.</summary>
		public CharacterEntity Character { get; set; }

		/// <summary>
		/// Dialogue template identifier. Part of the composite primary key.
		/// </summary>
		public int TemplateID { get; set; }

		/// <summary>
		/// Bitmask of choices already taken in this template, at most
		/// <c>DialogueTemplate.MaxTrackedChoices</c> (16) bits wide.
		/// </summary>
		public short Choices { get; set; }

		/// <summary>Row creation timestamp (UTC).</summary>
		public DateTime TimeCreated { get; set; }

		/// <summary>Timestamp of the most recent merge into this row (UTC).</summary>
		public DateTime TimeUpdated { get; set; }
	}
}
