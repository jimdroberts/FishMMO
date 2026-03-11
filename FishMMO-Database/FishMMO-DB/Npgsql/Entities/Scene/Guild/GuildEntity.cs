using System;
using System.Collections.Generic;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// Database entity representing a guild.
	/// </summary>
	public class GuildEntity
	{
		/// <summary>
		/// Primary key.
		/// </summary>
		public long ID { get; set; }

		/// <summary>
		/// Display name.
		/// </summary>
		public string Name { get; set; }

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
		/// Row creation timestamp (UTC).
		/// </summary>
		public DateTime TimeCreated { get; set; }

		/// <summary>
		/// Gets or sets the guild membership relationships.
		/// </summary>
		public List<CharacterGuildEntity> Characters { get; set; }
	}
}