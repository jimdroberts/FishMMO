using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One page of the portals a character has activated for themselves in one scene. See
	/// <see cref="Data.CharacterPortalData"/> for the shape and its reasons.
	/// </summary>
	/// <remarks>
	/// No <c>Version</c> and no soft-delete, like <see cref="CharacterWaypointEntity"/>: the row is
	/// a monotonically growing bitmask merged with OR.
	/// </remarks>
	public class CharacterPortalEntity
	{
		/// <summary>Owning character. Part of the primary key.</summary>
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }

		/// <summary>The scene the portals stand in. Part of the primary key.</summary>
		public string SceneName { get; set; }

		/// <summary>Which 64-portal page of the scene. Part of the primary key.</summary>
		public short Page { get; set; }

		/// <summary>
		/// The activated bits, stored as <c>bigint</c>; the two's-complement reinterpretation is
		/// lossless and done in the service, because PostgreSQL has no unsigned integer.
		/// </summary>
		public long Mask { get; set; }

		public DateTime TimeCreated { get; set; }
		public DateTime TimeUpdated { get; set; }
	}
}
