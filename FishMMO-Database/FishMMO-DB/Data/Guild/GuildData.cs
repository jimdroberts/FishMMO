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
		/// Recruitment blurb shown in the directory to non-members.
		/// </summary>
		public readonly string Blurb;

		/// <summary>
		/// Comma-separated recruitment tags, lower-cased.
		/// </summary>
		public readonly string Tags;

		/// <summary>
		/// Whether the guild is listed in the recruitment directory.
		/// </summary>
		public readonly bool IsRecruiting;

		/// <summary>
		/// Initializes a new instance of the <see cref="GuildData"/> struct.
		/// </summary>
		/// <remarks>
		/// The recruitment fields default so that the callers which only care about name, notice
		/// and message of the day are unchanged.
		/// </remarks>
		public GuildData(long id, string name, string notice, string messageOfTheDay, string blurb = "", string tags = "", bool isRecruiting = false)
		{
			ID = id;
			Name = name;
			Notice = notice;
			MessageOfTheDay = messageOfTheDay;
			Blurb = blurb;
			Tags = tags;
			IsRecruiting = isRecruiting;
		}
	}
}