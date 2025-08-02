namespace FishMMO.Shared
{
	/// <summary>
	/// Flags representing various character states and conditions.
	/// Used for bitwise state management and quick checks of character status.
	/// </summary>
	public enum CharacterFlags : int
	{
		/// <summary>
		/// Character is idle and not performing any actions.
		/// </summary>
		Idle = 0,
		/// <summary>
		/// Character is moving.
		/// </summary>
		IsMoving,
		/// <summary>
		/// Character is running.
		/// </summary>
		IsRunning,
		/// <summary>
		/// Character is crouching.
		/// </summary>
		IsCrouching,
		/// <summary>
		/// Character is swimming.
		/// </summary>
		IsSwimming,
		/// <summary>
		/// Character is teleporting.
		/// </summary>
		IsTeleporting,
		/// <summary>
		/// Character is frozen and cannot move.
		/// </summary>
		IsFrozen,
		/// <summary>
		/// Character is stunned and unable to act.
		/// </summary>
		IsStunned,
		/// <summary>
		/// Character is mesmerized and unable to act.
		/// </summary>
		IsMesmerized,
		/// <summary>
		/// Character is currently inside an instance.
		/// </summary>
		IsInInstance,
	}
}