using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Party update data transfer object.
	/// </summary>
	public struct PartyUpdateData
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;
		/// <summary>Party that was updated.</summary>
		public readonly long PartyID;
		/// <summary>Timestamp of the last update.</summary>
		public readonly DateTime LastUpdate;

		public PartyUpdateData(long id, long partyID, DateTime lastUpdate)
		{
			ID = id;
			PartyID = partyID;
			LastUpdate = lastUpdate;
		}
	}
}