namespace FishMMO.Database.Data
{
	/// <summary>
	/// Quest data transfer object.
	/// </summary>
	public struct QuestData
	{
		public readonly long ID;
		public readonly string Name;

		public QuestData(long id, string name)
		{
			ID = id;
			Name = name;
		}
	}
}