namespace FishMMO.Database.Data
{
	/// <summary>
	/// Party data transfer object.
	/// </summary>
	public struct PartyData
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;

		/// <summary>
		/// World server this party belongs to. A party exists on exactly one.
		/// </summary>
		public readonly long WorldServerID;

		public PartyData(long id, long worldServerID = 0)
		{
			ID = id;
			WorldServerID = worldServerID;
		}
	}
}