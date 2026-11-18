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
		/// The character's position on the guild's rank ladder. Zero indicates no guild.
		/// </summary>
		/// <remarks>
		/// This was a <c>GuildRank</c> enum value. Ranks are now rows a guild owns, so there is no
		/// fixed set of names an ECA condition could compare against — the ladder position is the
		/// only thing that means the same in every guild. A designer asking "is this character
		/// senior?" wants <see cref="Permissions"/>; one asking "how high are they?" wants this.
		/// </remarks>
		public byte RankOrder { get; }

		/// <summary>
		/// The permissions the character's rank holds.
		/// </summary>
		/// <remarks>
		/// Included alongside the order because a rank's POSITION and its POWERS are no longer the
		/// same fact. Rank 2 in one guild may invite and rank 2 in another may not, so a trigger
		/// that wants to fire for "an officer" has to ask about the permission, not the number.
		/// </remarks>
		public GuildPermissions Permissions { get; }

		/// <summary>
		/// Creates a new GuildEventData.
		/// </summary>
		/// <param name="initiator">The character joining or leaving the guild.</param>
		/// <param name="guildID">The guild ID.</param>
		/// <param name="rankOrder">The ladder position in the guild.</param>
		/// <param name="permissions">The permissions that rank holds.</param>
		public GuildEventData(ICharacter initiator, long guildID, byte rankOrder, GuildPermissions permissions)
			: base(initiator)
		{
			GuildID = guildID;
			RankOrder = rankOrder;
			Permissions = permissions;
		}
	}
}
