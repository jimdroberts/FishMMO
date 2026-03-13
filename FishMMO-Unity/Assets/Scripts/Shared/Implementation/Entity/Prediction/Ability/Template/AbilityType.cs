namespace FishMMO.Shared
{
	/// <summary>
	/// Represents the type of ability, such as physical or magical, and whether it is grounded or aerial.
	/// </summary>
	public enum AbilityType : int
	{
		/// <summary>
		/// No ability type.
		/// </summary>
		None = 0,

		/// <summary>
		/// Physical ability type.
		/// </summary>
		Physical,

		/// <summary>
		/// Magical ability type.
		/// </summary>
		Magic,

		/// <summary>
		/// Grounded physical ability type.
		/// </summary>
		GroundedPhysical,

		/// <summary>
		/// Grounded magical ability type.
		/// </summary>
		GroundedMagic,

		/// <summary>
		/// Aerial physical ability type.
		/// </summary>
		AerialPhysical,

		/// <summary>
		/// Aerial magical ability type.
		/// </summary>
		AerialMagic,
	}
}