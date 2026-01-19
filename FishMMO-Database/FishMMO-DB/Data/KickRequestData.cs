using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Kick request data transfer object.
	/// </summary>
	public struct KickRequestData
	{
		public readonly long ID;
		public readonly string AccountName;
		public readonly DateTime TimeCreated;

		public KickRequestData(long id, string accountName, DateTime timeCreated)
		{
			ID = id;
			AccountName = accountName;
			TimeCreated = timeCreated;
		}
	}
}