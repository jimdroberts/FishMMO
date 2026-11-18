namespace FishMMO.Shared
{
	/// <summary>
	/// One attribute value restored from the database and staged onto a <see cref="Pet"/> for
	/// application at spawn.
	/// </summary>
	/// <remarks>
	/// A plain shared-layer struct rather than the database DTO. The pet system translates the
	/// row into this before handing it over, which keeps <c>FishMMO.Database</c> out of the
	/// gameplay assembly that also ships to clients.
	/// </remarks>
	public struct PetPersistedAttribute
	{
		/// <summary>The attribute template this value belongs to.</summary>
		public int TemplateID;

		/// <summary>The attribute's base value.</summary>
		public int Value;

		/// <summary>
		/// The current value, for resource attributes. Meaningless for a plain attribute, which
		/// is why the template — not this field — decides which kind is being restored.
		/// </summary>
		public float CurrentValue;
	}

	/// <summary>
	/// One buff restored from the database and staged onto a <see cref="Pet"/> for application at
	/// spawn.
	/// </summary>
	public struct PetPersistedBuff
	{
		/// <summary>The buff template.</summary>
		public int TemplateID;

		/// <summary>Seconds of duration left when the pet was saved.</summary>
		public double RemainingTime;

		/// <summary>Seconds until the next periodic tick when the pet was saved.</summary>
		public double TickTime;

		/// <summary>Stack count.</summary>
		public int Stacks;

		/// <summary>Ticks already fired, so cumulative periodic effects resume rather than restart.</summary>
		public int TickCount;
	}
}
