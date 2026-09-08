namespace FishMMO.Shared.Core
{
	/// <summary>
	/// The standard rows of an overhead nameplate, in the order they stack from the top down.
	/// </summary>
	/// <remarks>
	/// The values are the default stacking <see cref="NameplateLine.Order"/> of each row, not a
	/// dense 0..n index, and the gaps are the point: a developer adding a row of their own —
	/// a level, a health bar caption, a quest marker — picks any integer and it lands exactly
	/// where they put it, without a rebuild of this enum or a renumbering of everything below.
	/// Lower sorts higher on the plate, so the name is always the top line.
	/// </remarks>
	public enum NameplateSlot
	{
		/// <summary>The character or object name. The top line.</summary>
		Name = 0,

		/// <summary>The guild line, drawn under the name.</summary>
		GuildName = 100,

		/// <summary>What an object is — "Banker", "Merchant" — drawn under the guild line.</summary>
		InteractableType = 200,

		/// <summary>Transient state: casting, resting, away, in combat.</summary>
		Status = 300,

		/// <summary>
		/// The first key a developer's own rows may use. Anything at or above this is never
		/// written by FishMMO itself.
		/// </summary>
		Custom = 1000,
	}

	/// <summary>
	/// Per-slot presentation defaults.
	/// </summary>
	/// <remarks>
	/// Kept beside the enum rather than in the renderer so a developer reading the slot list can
	/// see how each row is drawn, and so both the runtime and the tests answer the question from
	/// the same table.
	/// </remarks>
	public static class NameplateSlots
	{
		/// <summary>
		/// The font scale a slot is drawn at, relative to the plate's own font size.
		/// </summary>
		/// <param name="slot">The slot being drawn.</param>
		/// <returns>A multiplier on the plate's resolved font size.</returns>
		/// <remarks>
		/// The name carries the plate, so it is drawn at full size and everything under it is
		/// drawn smaller — that difference is what makes the stack read as a name with detail
		/// beneath it rather than as four lines of equal weight.
		/// </remarks>
		public static float DefaultScale(NameplateSlot slot)
		{
			switch (slot)
			{
				case NameplateSlot.Name:
					return 1.0f;
				case NameplateSlot.GuildName:
					return 0.85f;
				case NameplateSlot.InteractableType:
					return 0.8f;
				case NameplateSlot.Status:
					return 0.8f;
				default:
					return 0.85f;
			}
		}

		/// <summary>
		/// Whether a slot takes the plate's name colour rather than its secondary colour.
		/// </summary>
		/// <param name="slot">The slot being drawn.</param>
		/// <returns>True when the slot is drawn in the name colour.</returns>
		public static bool UsesNameColor(NameplateSlot slot)
		{
			return slot == NameplateSlot.Name;
		}
	}
}
