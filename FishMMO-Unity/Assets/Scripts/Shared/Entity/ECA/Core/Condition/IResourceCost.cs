namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for conditions that represent a resource cost.
	/// Used by the ability system to aggregate total resource costs across all activation conditions and events.
	/// </summary>
	public interface IResourceCost
	{
		/// <summary>
		/// The character attribute template representing the resource (e.g., Mana, Stamina).
		/// </summary>
		CharacterAttributeTemplate ResourceTemplate { get; }

		/// <summary>
		/// The amount of the resource required.
		/// </summary>
		int ResourceAmount { get; }
	}
}