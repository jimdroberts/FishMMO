namespace FishMMO.Database.Data.Enums
{
	/// <summary>
	/// Chat channel enumeration for different message scopes.
	/// </summary>
	public enum ChatChannel
	{
		/// <summary>
		/// System messages.
		/// </summary>
		System = 0,

		/// <summary>
		/// Local area chat.
		/// </summary>
		Local = 1,

		/// <summary>
		/// Private tell/whisper.
		/// </summary>
		Tell = 2,

		/// <summary>
		/// Guild chat.
		/// </summary>
		Guild = 3,

		/// <summary>
		/// Party chat.
		/// </summary>
		Party = 4,

		/// <summary>
		/// World chat.
		/// </summary>
		World = 5,

		/// <summary>
		/// Trade channel.
		/// </summary>
		Trade = 6
	}
}