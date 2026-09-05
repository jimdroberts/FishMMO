using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One structure a player has built on a plot.
	/// </summary>
	/// <remarks>
	/// Positioned relative to its plot rather than in world space. A plot's location belongs to the
	/// scene and a designer may move it; world coordinates would leave the house standing in a field
	/// when that happened, while an offset carries it along.
	/// </remarks>
	public class PlotStructureEntity : IVersionedEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>
		/// Application-level concurrency token. Incremented on every write to detect stale updates.
		/// </summary>
		public long Version { get; set; }

		/// <summary>The plot this structure stands on.</summary>
		public long PlotID { get; set; }

		/// <summary>Which structure was built.</summary>
		public int TemplateID { get; set; }

		/// <summary>Offset east of the plot's origin, in metres.</summary>
		public float LocalX { get; set; }

		/// <summary>Offset above the plot's origin, in metres.</summary>
		public float LocalY { get; set; }

		/// <summary>Offset north of the plot's origin, in metres.</summary>
		public float LocalZ { get; set; }

		/// <summary>
		/// Rotation about the vertical axis, in degrees.
		/// </summary>
		/// <remarks>
		/// Yaw alone, not a quaternion. Structures stand on the ground, so pitch and roll have no
		/// meaning here beyond letting a player leave a house lying on its side — and a single float
		/// is a quarter of the storage and cannot be denormalised into something invalid.
		/// </remarks>
		public float Yaw { get; set; }

		/// <summary>Row creation timestamp (UTC).</summary>
		public DateTime TimeCreated { get; set; }
	}
}
