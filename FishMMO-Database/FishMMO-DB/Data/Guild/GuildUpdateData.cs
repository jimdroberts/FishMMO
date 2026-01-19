using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Guild update data transfer object.
	/// </summary>
	public struct GuildUpdateData
	{
		public readonly long ID;
		public readonly long GuildID;
		public readonly DateTime LastUpdate;

		public GuildUpdateData(long id, long guildID, DateTime lastUpdate)
		{
			ID = id;
			GuildID = guildID;
			LastUpdate = lastUpdate;
		}
	}
}