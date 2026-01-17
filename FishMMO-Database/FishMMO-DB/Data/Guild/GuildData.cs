namespace FishMMO.Database.Data
{
	/// <summary>
	/// Guild data transfer object.
	/// </summary>
	public struct GuildData
	{
		public long ID { get; set; }
		public string Name { get; set; }
		public string Notice { get; set; }
	}
}