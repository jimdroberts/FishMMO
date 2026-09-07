using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One page of a character's discovered waypoints in one scene. See
	/// <see cref="Data.CharacterWaypointData"/> for the shape and its reasons.
	/// </summary>
	/// <remarks>
	/// No <c>Version</c> and no soft-delete, like <see cref="CharacterDialogueChoiceEntity"/>: the
	/// row is a monotonically growing bitmask merged with OR, so there is no stale write to reject
	/// and nothing a delete-and-recreate would express better than a zero row.
	/// </remarks>
	public class CharacterWaypointEntity
	{
		/// <summary>Owning character. Part of the primary key.</summary>
		public long CharacterID { get; set; }
		public CharacterEntity Character { get; set; }

		/// <summary>The scene the waypoints stand in. Part of the primary key.</summary>
		public string SceneName { get; set; }

		/// <summary>Which 64-waypoint page of the scene. Part of the primary key.</summary>
		public short Page { get; set; }

		/// <summary>
		/// The discovered bits. Stored as <c>bigint</c>; the two's-complement reinterpretation is
		/// lossless and done in the service, because PostgreSQL has no unsigned integer.
		/// </summary>
		public long Mask { get; set; }

		public DateTime TimeCreated { get; set; }
		public DateTime TimeUpdated { get; set; }
	}
}
