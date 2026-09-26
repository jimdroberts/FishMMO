namespace FishMMO.Database.Data
{
	/// <summary>
	/// What became of one row of a batched character save.
	/// </summary>
	/// <remarks>
	/// The same distinctions the single-row save reports as error codes, per row, because one
	/// statement now speaks for many characters and each needs its own answer: a lost claim means
	/// the caller must stop simulating that character, a stale row means a newer write is already
	/// stored, and neither says anything about the rows around it.
	/// </remarks>
	public enum CharacterPersistOutcome : byte
	{
		/// <summary>The row was written.</summary>
		Saved = 0,

		/// <summary>
		/// The row already holds exactly this version under this caller's claim: a replay of a write
		/// that landed, typically a transaction retried after its commit reply was lost. Nothing is
		/// missing, so callers treat it as <see cref="Saved"/>.
		/// </summary>
		Replayed = 1,

		/// <summary>The character does not exist or has been deleted.</summary>
		NotFound = 2,

		/// <summary>A newer version is already stored. Nothing is lost: the stored row is the newer of the two.</summary>
		Stale = 3,

		/// <summary>
		/// The caller supplied a claim and no longer holds it. Another server is authoritative for the
		/// character, and the caller must stop simulating it rather than retry.
		/// </summary>
		OwnershipLost = 4,

		/// <summary>The row was malformed (no id, no version, a claim for a different character) and was not attempted.</summary>
		Invalid = 5,
	}

	/// <summary>
	/// One character's outcome from a batched save.
	/// </summary>
	public readonly struct CharacterPersistResult
	{
		/// <summary>The character the outcome belongs to.</summary>
		public readonly long CharacterID;

		/// <summary>What became of its row.</summary>
		public readonly CharacterPersistOutcome Outcome;

		/// <summary>True when the row is in the database at the version that was sent.</summary>
		public bool IsStored => Outcome == CharacterPersistOutcome.Saved || Outcome == CharacterPersistOutcome.Replayed;

		/// <summary>
		/// Initializes a new outcome.
		/// </summary>
		/// <param name="characterID">The character.</param>
		/// <param name="outcome">What became of its row.</param>
		public CharacterPersistResult(long characterID, CharacterPersistOutcome outcome)
		{
			CharacterID = characterID;
			Outcome = outcome;
		}
	}
}
