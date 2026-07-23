namespace FishMMO.Shared
{
	/// <summary>
	/// Defines the lifecycle status of a quest instance for a character.
	/// </summary>
	public enum QuestStatus : byte
	{
		/// <summary>
		/// The quest has not been accepted yet.
		/// </summary>
		Inactive = 0,

		/// <summary>
		/// The quest has been accepted and is in progress.
		/// </summary>
		Active = 1,

		/// <summary>
		/// All objectives have been met and the quest is ready for turn-in.
		/// </summary>
		Complete = 2,

		/// <summary>
		/// The quest has been turned in and rewards have been granted.
		/// </summary>
		TurnedIn = 3,

		/// <summary>
		/// The quest was failed (e.g. timer expired).
		/// </summary>
		Failed = 4,
	}
}
