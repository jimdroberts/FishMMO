using System;

namespace FishMMO.Database.Npgsql.Entities
{
	/// <summary>
	/// One player's standing permission on somebody else's plot.
	/// </summary>
	/// <remarks>
	/// The owner never appears here. Ownership is not a grant: it cannot be revoked by deleting a
	/// row, and a house whose owner's key lived in the same table as their friends' would be one
	/// bad delete away from being a house its owner could not enter. Who owns a plot is on the plot
	/// row; this table is only about the people they have let in.
	///
	/// <para>Rows are per plot, not per owner. A player who loses a plot and buys another does not
	/// bring their old guest list with them — the grants were made about a particular house, and
	/// carrying them over would silently re-admit people to somewhere they were never invited.</para>
	/// </remarks>
	public class PlotAccessEntity : IVersionedEntity
	{
		/// <summary>Primary key.</summary>
		public long ID { get; set; }

		/// <summary>
		/// Application-level concurrency token. Incremented on every write to detect stale updates.
		/// </summary>
		public long Version { get; set; }

		/// <summary>The plot this grant is about.</summary>
		public long PlotID { get; set; }

		/// <summary>The character being granted access.</summary>
		public long CharacterID { get; set; }

		/// <summary>
		/// What they may do, as a <c>FishMMO.Shared.PlotPermission</c> bitmask.
		/// </summary>
		/// <remarks>
		/// An integer rather than a set of boolean columns, because permissions are read as a unit —
		/// every question asked of this table is "what may this person do here", never "who may
		/// place items" — and because adding one should not be a migration.
		///
		/// <para>Unknown bits are meaningless and are masked off on read by
		/// <c>PlotAccess.Sanitize</c>. A permission retired in a later version therefore stops
		/// meaning anything rather than quietly becoming whichever permission is given its bit
		/// next.</para>
		/// </remarks>
		public int Permissions { get; set; }

		/// <summary>
		/// Who granted it.
		/// </summary>
		/// <remarks>
		/// Kept for the owner's benefit rather than the system's: a plot with
		/// <c>InviteFriends</c> handed out is a plot whose guest list the owner did not write all
		/// of, and "who let this person in" is the first thing they will ask.
		/// </remarks>
		public long GrantedByCharacterID { get; set; }

		/// <summary>When the grant was made or last changed (UTC).</summary>
		public DateTime TimeGranted { get; set; }
	}
}
