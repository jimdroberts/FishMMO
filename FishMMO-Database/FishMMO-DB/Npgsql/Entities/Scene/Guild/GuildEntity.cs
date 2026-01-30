using System;
using System.Collections.Generic;

namespace FishMMO.Database.Npgsql.Entities
{
	public class GuildEntity
	{
		public long ID { get; set; }
		public string Name { get; set; }
		/// <remarks>
		/// This value is stored as a computed column in PostgreSQL (generated from <see cref="Name"/>).
		/// It exists to support efficient case-insensitive lookups and uniqueness enforcement.
		/// </remarks>
		public string NameLowercase { get; set; }
		public string Notice { get; set; }
		public DateTime TimeCreated { get; set; }

		/// <summary>
		/// Gets or sets the guild membership relationships.
		/// </summary>
		public List<CharacterGuildEntity> Characters { get; set; }
	}
}