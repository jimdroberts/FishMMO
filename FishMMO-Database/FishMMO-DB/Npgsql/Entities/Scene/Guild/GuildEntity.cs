using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace FishMMO.Database.Npgsql.Entities
{
	[Table("guilds")]
	public class GuildEntity
	{
		/// <summary>
		/// Gets or sets the primary key.
		/// </summary>
		public long ID { get; set; }

		/// <summary>
		/// Gets or sets the guild name.
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Gets or sets the normalized (lowercase) guild name.
		/// </summary>
		/// <remarks>
		/// This value is stored as a computed column in PostgreSQL (generated from <see cref="Name"/>).
		/// It exists to support efficient case-insensitive lookups and uniqueness enforcement.
		/// </remarks>
		public string NameLowercase { get; set; }

		/// <summary>
		/// Gets or sets the guild notice/message.
		/// </summary>
		public string Notice { get; set; }

		/// <summary>
		/// Gets or sets the creation time.
		/// </summary>
		public DateTime TimeCreated { get; set; }

		/// <summary>
		/// Gets or sets the guild membership relationships.
		/// </summary>
		public List<CharacterGuildEntity> Characters { get; set; }
	}
}