namespace FishMMO.Shared
{
	/// <summary>
	/// Represents the current state of a capturable objective.
	/// </summary>
	public enum ObjectiveState : byte
	{
		/// <summary>
		/// No team or player controls this objective.
		/// </summary>
		Neutral = 0,

		/// <summary>
		/// An active capture attempt is in progress.
		/// </summary>
		Capturing = 1,

		/// <summary>
		/// The objective has been captured and is owned by a player or team.
		/// </summary>
		Captured = 2,

		/// <summary>
		/// Multiple opposing players or teams are contesting this objective.
		/// </summary>
		Contested = 3,
	}
}