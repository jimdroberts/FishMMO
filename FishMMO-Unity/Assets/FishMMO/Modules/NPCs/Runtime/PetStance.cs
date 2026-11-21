namespace FishMMO.Shared
{
	/// <summary>
	/// How willing a pet is to start a fight on its own.
	/// </summary>
	/// <remarks>
	/// The stance only governs whether the pet <em>initiates</em>. An explicit attack command
	/// works in every stance, including <see cref="Passive"/>.
	/// </remarks>
	public enum PetStance : byte
	{
		/// <summary>
		/// Never engages by itself. Will not even fight back when struck — it stays on its owner
		/// and waits to be told. The safe default for a pet the player is leading through
		/// dangerous territory.
		/// </summary>
		Passive = 0,

		/// <summary>
		/// Fights back. Engages anything that attacks the pet or its owner, but never picks a
		/// fight with a hostile that has not acted. This is the default.
		/// </summary>
		Defensive = 1,

		/// <summary>
		/// Hunts. Engages any hostile that comes within the attacking state's detection radius,
		/// whether or not it has done anything.
		/// </summary>
		Aggressive = 2,
	}

	/// <summary>
	/// What a pet is currently being told to do about movement, independent of its
	/// <see cref="PetStance"/>.
	/// </summary>
	public enum PetMovementOrder : byte
	{
		/// <summary>Stay at the owner's heel. The default.</summary>
		Follow = 0,

		/// <summary>Hold the position it was standing in when ordered. Does not follow the owner.</summary>
		Stay = 1,
	}
}
