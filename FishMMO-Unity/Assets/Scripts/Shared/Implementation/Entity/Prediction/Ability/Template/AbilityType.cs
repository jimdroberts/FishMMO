namespace FishMMO.Shared
{
	/// <summary>
	/// Represents the type of ability, such as physical or magical, and whether it is grounded or aerial.
	/// Also drives animation triggers via <see cref="CharacterAnimationController"/>.
	/// </summary>
	public enum AbilityType : int
	{
		/// <summary>
		/// No ability type.
		/// </summary>
		None = 0,

		/// <summary>
		/// Physical ability type (melee strike, weapon swing, etc.). Triggers Attack animation.
		/// </summary>
		Physical,

		/// <summary>
		/// Magical ability type (spell, heal, AoE blast, etc.). Triggers Cast animation.
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

		/// <summary>
		/// Block ability — raises shield/guard. Triggers SetBlocking(true) on start, SetBlocking(false) on cancel.
		/// </summary>
		Block,

		/// <summary>
		/// Roll/dodge ability — evasive movement. Triggers Roll animation.
		/// </summary>
		Roll,
	}
}