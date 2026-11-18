namespace FishMMO.Shared
{
	/// <summary>
	/// Defines the alliance level between two factions (e.g., Ally, Neutral, Enemy).
	/// Used in faction matrices to determine relationships and interactions.
	/// </summary>
	public enum FactionAllianceLevel : byte
	{
		/// <summary>
		/// Factions are allies and cooperate with each other.
		/// </summary>
		Ally = 0,

		/// <summary>
		/// Factions are neutral and do not interact positively or negatively.
		/// </summary>
		Neutral,

		/// <summary>
		/// Factions are enemies and are hostile toward each other.
		/// </summary>
		Enemy,
	}
}