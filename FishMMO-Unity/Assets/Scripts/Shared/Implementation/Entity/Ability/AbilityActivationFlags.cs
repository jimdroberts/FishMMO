namespace FishMMO.Shared
{
	/// <summary>
	/// Flags representing the activation state of an ability.
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
	}
}