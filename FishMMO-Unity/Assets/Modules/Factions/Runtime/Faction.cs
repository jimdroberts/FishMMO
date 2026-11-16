namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a character's standing or reputation with a specific faction.
	/// Holds the current value and reference to the faction template.
	/// </summary>
	public class Faction
	{
		/// <summary>
		/// Persistence version. Advanced by every standing change (<see cref="MarkChanged"/>) and
		/// again by the save snapshot; compared by <see cref="MarkPersisted"/> so a change made
		/// while a write was in flight keeps the row dirty. Loaded from the database row on login.
		/// </summary>
		/// <remarks>
		/// Before 2026-09-07 nothing advanced this and nothing wrote factions back at all: every
		/// kill credit and quest reward was lost on logout. The mutation-side bump is load-bearing
		/// for the same reason it is on <c>Achievement</c> — a version advanced only by the
		/// snapshot cannot guard an in-flight change.
		/// </remarks>
		public long Version;

		/// <summary>Whether this standing has changed since the database last confirmed it.</summary>
		public bool PersistenceDirty { get; private set; }

		/// <summary>Records a mutation: dirty, and a new version so a stale confirmation cannot clear it.</summary>
		public void MarkChanged()
		{
			PersistenceDirty = true;
			++Version;
		}

		/// <summary>Clears the dirty mark if nothing has changed since the confirmed snapshot was taken.</summary>
		public void MarkPersisted(long persistedVersion)
		{
			if (Version == persistedVersion)
			{
				PersistenceDirty = false;
			}
		}

		/// <summary>
		/// The current reputation or standing value for this faction.
		/// Clamped between FactionTemplate.Minimum and FactionTemplate.Maximum.
		/// </summary>
		public int Value;

		/// <summary>
		/// The template defining this faction's properties and relationships.
		/// </summary>
		public FactionTemplate Template { get; private set; }

		/// <summary>
		/// Constructs a new Faction instance from a template ID and initial value.
		/// Looks up the template and clamps the value to valid bounds.
		/// </summary>
		/// <param name="templateID">The template ID for the faction.</param>
		/// <param name="value">Initial reputation or standing value.</param>
		public Faction(int templateID, int value)
		{
			Template = FactionTemplate.Get<FactionTemplate>(templateID);
			Value = value.Clamp(FactionTemplate.Minimum, FactionTemplate.Maximum);
		}
	}
}