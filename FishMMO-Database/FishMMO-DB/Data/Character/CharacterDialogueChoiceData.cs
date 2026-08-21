namespace FishMMO.Database.Data
{
	/// <summary>
	/// One character's persisted "already chosen" bitmask for a single dialogue template.
	/// </summary>
	/// <remarks>
	/// <para><b>Why a bitmask and not a row per choice.</b> A dialogue template can track at most
	/// <c>DialogueTemplate.MaxTrackedChoices</c> (16) choices, and both the server session and the
	/// wire format (<c>DialogueStartBroadcast.CachedChoices</c>) already carry that state as a
	/// single <see cref="short"/>. A row-per-choice table would store the same 16 bits as up to 16
	/// rows of roughly 40 bytes each — a ~300x expansion of a value that is read and written whole,
	/// never queried by individual bit — and every read would have to fold the rows back into the
	/// mask the caller actually wants. The mask is stored as it is used.</para>
	///
	/// <para><b>Volume.</b> One row per character per <em>caching</em> template, so the table is
	/// bounded by (characters x templates with <c>CacheDialogueChoices</c> enabled) — not by the
	/// number of conversations held, which is what an event-log shape would have grown with. Rows
	/// are only ever created for templates that opt in.</para>
	///
	/// <para><b>When dialogue assets change.</b> The bit index comes from
	/// <c>DialogueTemplate.GetChoiceBitIndex</c>, which counts choices positionally in authoring
	/// order. Appending a node or a choice at the end is safe: existing bits keep their meaning.
	/// Inserting or deleting a choice in the middle, or reordering nodes, shifts every later bit,
	/// so a stored mask would then describe different choices than the ones the player made — the
	/// failure mode is a one-time reward appearing already-taken, or an exhausted one becoming
	/// available again. That is an authoring-time hazard, not something the store can detect, so
	/// the service exposes <c>ResetTemplateAsync</c> for content to be re-cut deliberately.</para>
	///
	/// <para><b>When a template is removed.</b> Its rows are left in place. A template ID that does
	/// not resolve on <em>this</em> process is not evidence the asset was deleted — scene servers
	/// load different content sets, and a load failure looks identical — so deleting on a lookup
	/// miss would quietly destroy live players' one-time-choice history during a partial content
	/// rollout. An orphaned row costs two bytes and is invisible; that is the cheaper mistake.</para>
	/// </remarks>
	public struct CharacterDialogueChoiceData
	{
		/// <summary>Character that made the choices.</summary>
		public readonly long CharacterID;

		/// <summary>Dialogue template ID (a <c>CachedScriptableObject</c> ID).</summary>
		public readonly int TemplateID;

		/// <summary>
		/// Bitmask of choices already taken in this template. Bit N corresponds to the Nth choice
		/// in authoring order, as computed by <c>DialogueTemplate.GetChoiceBitIndex</c>.
		/// </summary>
		public readonly short Choices;

		/// <summary>
		/// Initializes a dialogue choice mask.
		/// </summary>
		/// <param name="characterID">Character that made the choices.</param>
		/// <param name="templateID">Dialogue template ID.</param>
		/// <param name="choices">Bitmask of choices already taken.</param>
		public CharacterDialogueChoiceData(long characterID, int templateID, short choices)
		{
			CharacterID = characterID;
			TemplateID = templateID;
			Choices = choices;
		}
	}
}
