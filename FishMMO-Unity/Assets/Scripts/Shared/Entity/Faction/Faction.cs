namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a character's standing or reputation with a specific faction.
	/// Holds the current value and reference to the faction template.
	/// </summary>
	public class Faction
	{
		/// <summary>
		/// Version number for this faction instance, used for client synchronization and updates.
		/// Incremented whenever the faction's state changes in a way that requires client updates (
		/// e.g., value changes that affect the faction's standing).
		/// Not incremented for changes that do not affect client state (e.g., internal
		/// tracking of reputation changes that doesn't meet the next update threshold).
		/// </summary>
		public long Version;

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