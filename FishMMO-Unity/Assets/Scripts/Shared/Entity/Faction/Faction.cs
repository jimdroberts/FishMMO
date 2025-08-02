namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a character's standing or reputation with a specific faction.
	/// Holds the current value and reference to the faction template.
	/// </summary>
	public class Faction
	{
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