namespace FishMMO.Database.Data
{
	/// <summary>
	/// Guild data transfer object.
	/// </summary>
	public struct GuildData
	{
		public readonly long ID;
		public readonly string Name;
		public readonly string Notice;

		public GuildData(long id, string name, string notice)
		{
			ID = id;
			Name = name;
			Notice = notice;
		}
	}
}