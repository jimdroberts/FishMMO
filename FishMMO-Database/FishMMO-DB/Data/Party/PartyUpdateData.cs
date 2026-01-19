using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Party update data transfer object.
	/// </summary>
	public struct PartyUpdateData
	{
		public readonly long ID;
		public readonly long PartyID;
		public readonly DateTime LastUpdate;

		public PartyUpdateData(long id, long partyID, DateTime lastUpdate)
		{
			ID = id;
			PartyID = partyID;
			LastUpdate = lastUpdate;
		}
	}
}