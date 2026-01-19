namespace FishMMO.Database.Data
{
	/// <summary>
	/// Party data transfer object.
	/// </summary>
	public struct PartyData
	{
		public readonly long ID;

		public PartyData(long id)
		{
			ID = id;
		}
	}
}