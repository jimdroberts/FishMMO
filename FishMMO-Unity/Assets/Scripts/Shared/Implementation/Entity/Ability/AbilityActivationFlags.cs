namespace FishMMO.Shared
{
	/// <summary>
	/// Flags representing the activation state of an ability.
	/// Stored as bit positions in an int and manipulated via IntBitExtensions.
	/// </summary>
	public enum AbilityActivationFlags : int
	{
		/// <summary>
		/// Indicates the data is actual activation data.
		/// </summary>
		IsActualData = 0,

		/// <summary>
		/// Indicates the ability was interrupted.
		/// </summary>
		Interrupt,

		/// <summary>
		/// Indicates the activation key is held (for charged/channeled abilities).
		/// </summary>
		IsHeld,

		/// <summary>
		/// Indicates the activation is for a consumable item.
		/// </summary>
		IsConsumable,

		/// <summary>
		/// Indicates the activation is for a mount.
		/// </summary>
		IsMount,
	}
}