using System;
using System.Collections.Generic;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Database entity representing a guild.
	/// </summary>
	public class GuildEntity : IVersionedEntity
	{
		/// <summary>
		/// Primary key.
		/// </summary>
		public long ID { get; set; }

		/// <summary>
		/// Application-level concurrency token. Incremented on every write to detect stale updates.
		/// </summary>
		public long Version { get; set; }

		/// <summary>
		/// Display name.
		/// </summary>
		public string Name { get; set; }

		/// <summary>Lowercase copy of guild name for lookups.</summary>
		/// <remarks>
		/// This value is stored as a computed column in PostgreSQL (generated from <see cref="Name"/>).
		/// It exists to support efficient case-insensitive lookups and uniqueness enforcement.
		/// </remarks>
		public string NameLowercase { get; set; }

		/// <summary>
		/// Guild notice text.
		/// </summary>
		public string Notice { get; set; }

		/// <summary>
		/// Message of the day displayed to members on login.
		/// </summary>
		public string MessageOfTheDay { get; set; }

		/// <summary>
		/// Recruitment advertisement shown in the guild directory.
		/// </summary>
		/// <remarks>
		/// Separate from <see cref="Notice"/> and <see cref="MessageOfTheDay"/> on purpose. Those
		/// two are written for people who are already members; this one is written for people who
		/// are not, and a guild that had to advertise with its internal notice would either leak
		/// its business or stop using the notice.
		/// </remarks>
		public string Blurb { get; set; }

		/// <summary>
		/// Comma-separated recruitment tags, lower-cased, used by directory search.
		/// </summary>
		public string Tags { get; set; }

		/// <summary>
		/// Whether the guild is listed in the recruitment directory and accepting applications.
		/// </summary>
		public bool IsRecruiting { get; set; }

		/// <summary>
		/// Row creation timestamp (UTC).
		/// </summary>
		public DateTime TimeCreated { get; set; }

		/// <summary>
		/// Gets or sets the guild membership relationships.
		/// </summary>
		public List<CharacterGuildEntity> Characters { get; set; }

		/// <summary>
		/// Gets or sets the guild's editable rank rows.
		/// </summary>
		public List<GuildRankEntity> Ranks { get; set; }

		/// <summary>
		/// Gets or sets the guild's pending recruitment applications.
		/// </summary>
		public List<GuildApplicationEntity> Applications { get; set; }
	}
}