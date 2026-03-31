using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA event data for guild join and leave events.
	/// </summary>
	public class GuildEventData : EventData
	{
		/// <summary>
		/// The guild ID. Zero indicates no guild.
		/// </summary>
		public long GuildID { get; }

		/// <summary>
		/// The rank of the character in the guild.
		/// </summary>
		public GuildRank Rank { get; }

		/// <summary>
		/// Creates a new GuildEventData.
		/// </summary>
		/// <param name="initiator">The character joining or leaving the guild.</param>
		/// <param name="guildID">The guild ID.</param>
		/// <param name="rank">The rank in the guild.</param>
		public GuildEventData(ICharacter initiator, long guildID, GuildRank rank)
			: base(initiator)
		{
			GuildID = guildID;
			Rank = rank;
		}
	}
}
