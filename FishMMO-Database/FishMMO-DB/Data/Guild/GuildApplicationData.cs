using System;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// One pending application to join a guild.
	/// </summary>
	/// <remarks>
	/// Only PENDING applications exist as rows. A decided application is deleted rather than
	/// flagged: the queue is a work list, an accepted application is already represented by the
	/// membership row it produced, and a declined one is represented by the log entry. Keeping
	/// decided rows would mean every read of the queue has to filter them and every unique
	/// constraint has to account for them.
	/// </remarks>
	public struct GuildApplicationData
	{
		/// <summary>Primary key. Zero for a row that has not been written yet.</summary>
		public readonly long ID;

		/// <summary>The guild applied to.</summary>
		public readonly long GuildID;

		/// <summary>The applying character.</summary>
		public readonly long CharacterID;

		/// <summary>The applicant's message. May be empty.</summary>
		public readonly string Message;

		/// <summary>When the application was submitted (UTC).</summary>
		public readonly DateTime TimeCreated;

		/// <summary>
		/// Initializes a new application row.
		/// </summary>
		/// <param name="id">Primary key, or zero when not yet written.</param>
		/// <param name="guildID">The guild applied to.</param>
		/// <param name="characterID">The applying character.</param>
		/// <param name="message">The applicant's message.</param>
		/// <param name="timeCreated">Submission time (UTC).</param>
		public GuildApplicationData(long id, long guildID, long characterID, string message, DateTime timeCreated)
		{
			ID = id;
			GuildID = guildID;
			CharacterID = characterID;
			Message = message;
			TimeCreated = timeCreated;
		}
	}

	/// <summary>
	/// One guild as it appears in the recruitment directory.
	/// </summary>
	public struct GuildDirectoryEntryData
	{
		/// <summary>Guild identifier.</summary>
		public readonly long ID;

		/// <summary>Guild display name.</summary>
		public readonly string Name;

		/// <summary>Recruitment blurb.</summary>
		public readonly string Blurb;

		/// <summary>Comma-separated recruitment tags.</summary>
		public readonly string Tags;

		/// <summary>Current member count.</summary>
		public readonly int MemberCount;

		/// <summary>
		/// Initializes a new directory entry.
		/// </summary>
		/// <param name="id">Guild identifier.</param>
		/// <param name="name">Guild display name.</param>
		/// <param name="blurb">Recruitment blurb.</param>
		/// <param name="tags">Comma-separated tags.</param>
		/// <param name="memberCount">Current member count.</param>
		public GuildDirectoryEntryData(long id, string name, string blurb, string tags, int memberCount)
		{
			ID = id;
			Name = name;
			Blurb = blurb;
			Tags = tags;
			MemberCount = memberCount;
		}
	}
}
