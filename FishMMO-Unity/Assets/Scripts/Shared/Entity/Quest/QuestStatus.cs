namespace FishMMO.Shared
{
	/// <summary>
	/// Defines the status of a quest instance for a character.
	/// </summary>
	public enum QuestStatus : byte
	{
		/// <summary>
		/// The quest is not yet started or acquired.
		/// </summary>
		Inactive = 0,

		/// <summary>
		/// The quest is currently active and in progress.
		/// </summary>
		Active,

		/// <summary>
		/// The quest has been completed.
		/// </summary>
		Completed,
	}
}