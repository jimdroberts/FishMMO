namespace FishMMO.Database.Data
{
	/// <summary>
	/// Guild data transfer object.
	/// </summary>
	public struct GuildData
	{
		/// <summary>
		/// Guild identifier.
		/// </summary>
		public readonly long ID;

		/// <summary>
		/// Guild display name.
		/// </summary>
		public readonly string Name;

		/// <summary>
		/// Guild notice text.
		/// </summary>
		public readonly string Notice;

		/// <summary>
		/// Message of the day displayed to members on login.
		/// </summary>
		public readonly string MessageOfTheDay;

		/// <summary>
		/// Initializes a new instance of the <see cref="GuildData"/> struct.
		/// </summary>
		public GuildData(long id, string name, string notice, string messageOfTheDay)
		{
			ID = id;
			Name = name;
			Notice = notice;
			MessageOfTheDay = messageOfTheDay;
		}
	}
}