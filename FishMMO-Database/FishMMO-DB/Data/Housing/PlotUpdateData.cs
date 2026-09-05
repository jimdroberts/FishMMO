using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Plot update data transfer object.
	/// </summary>
	public struct PlotUpdateData
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;
		/// <summary>Plot that was updated.</summary>
		public readonly long PlotID;
		/// <summary>Timestamp of the last update.</summary>
		public readonly DateTime LastUpdate;

		public PlotUpdateData(long id, long plotID, DateTime lastUpdate)
		{
			ID = id;
			PlotID = plotID;
			LastUpdate = lastUpdate;
		}
	}
}
