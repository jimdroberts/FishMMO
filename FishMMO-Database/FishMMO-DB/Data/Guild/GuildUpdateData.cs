using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Guild update data transfer object.
	/// </summary>
	public struct GuildUpdateData
	{
		/// <summary>Primary key.</summary>
		public readonly long ID;
		/// <summary>Guild that was updated.</summary>
		public readonly long GuildID;
		/// <summary>Timestamp of the last update.</summary>
		public readonly DateTime LastUpdate;

		public GuildUpdateData(long id, long guildID, DateTime lastUpdate)
		{
			ID = id;
			GuildID = guildID;
			LastUpdate = lastUpdate;
		}
	}
}