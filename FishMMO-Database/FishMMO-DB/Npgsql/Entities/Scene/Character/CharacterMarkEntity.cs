using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// A character's marked location. See <see cref="Data.CharacterMarkData"/> for the shape and its reasons.
	/// </summary>
	public class CharacterMarkEntity
	{
		/// <summary>Owning character; the primary key (one mark per character).</summary>
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }

		/// <summary>The open-world scene marked.</summary>
		public string SceneName { get; set; }

		/// <summary>The scene row ID (channel) the mark was made in, or 0. A hint, never a foreign key.</summary>
		public long SceneID { get; set; }

		public float X { get; set; }
		public float Y { get; set; }
		public float Z { get; set; }

		/// <summary>Facing in degrees about the vertical axis.</summary>
		public float Heading { get; set; }

		public DateTime TimeUpdated { get; set; }
	}
}
