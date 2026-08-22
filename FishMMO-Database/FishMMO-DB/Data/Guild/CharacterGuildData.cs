using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// Character guild membership data transfer object.
	/// </summary>
	/// <remarks>
	/// <see cref="RaceID"/> and <see cref="LastOnlineUtc"/> are joined in from the character row
	/// rather than stored on the membership row. The roster needs them to render a member the
	/// reader can actually place — a name and a rank alone do not tell a leader who has stopped
	/// logging in — and duplicating them onto the membership row would mean two copies of a fact
	/// the character table already owns, drifting apart on every login.
	///
	/// They default so the many callers that CONSTRUCT a membership to persist it (create, accept,
	/// connect, disconnect) do not have to supply values the write path ignores; only the read
	/// path populates them.
	/// </remarks>
	public struct CharacterGuildData : IVersioned<CharacterGuildData>
	{
		public readonly long ID;
		public readonly long Version;
		public readonly long CharacterID;
		public readonly long GuildID;
		public readonly byte Rank;
		public readonly string Location;

		/// <summary>The member's race identifier, joined in from the character row.</summary>
		public readonly int RaceID;

		/// <summary>
		/// When the member's character row was last written (UTC), joined in from the character
		/// row. Used as a last-seen figure for members who are not connected.
		/// </summary>
		public readonly DateTime LastOnlineUtc;

		/// <summary>The member's level, joined in from the character row.</summary>
		public readonly int Level;

		/// <summary>Note about this member visible to every member of the guild.</summary>
		public readonly string PublicNote;

		/// <summary>
		/// Note about this member visible only to ranks holding <c>ViewOfficerNotes</c>.
		/// </summary>
		/// <remarks>
		/// Populated on every read. The permission filter is applied where the roster is put on
		/// the wire, not here — a service that silently blanked a column depending on who was
		/// asking would need to know who is asking, and this one deliberately does not.
		/// </remarks>
		public readonly string OfficerNote;

		long IVersioned<CharacterGuildData>.Version => Version;

		public CharacterGuildData(long id, long characterID, long guildID, byte rank, string location)
			: this(id, version: 0, characterID, guildID, rank, location)
		{
		}

		public CharacterGuildData(long id, long version, long characterID, long guildID, byte rank, string location, int raceID = 0, DateTime lastOnlineUtc = default, int level = 0, string publicNote = "", string officerNote = "")
		{
			ID = id;
			Version = version;
			CharacterID = characterID;
			GuildID = guildID;
			Rank = rank;
			Location = location;
			RaceID = raceID;
			LastOnlineUtc = lastOnlineUtc;
			Level = level;
			PublicNote = publicNote ?? string.Empty;
			OfficerNote = officerNote ?? string.Empty;
		}

		public CharacterGuildData WithVersion(long newVersion)
		{
			return new CharacterGuildData(ID, newVersion, CharacterID, GuildID, Rank, Location, RaceID, LastOnlineUtc, Level, PublicNote, OfficerNote);
		}
	}
}
