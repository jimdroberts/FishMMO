namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for polymorphic integer value providers used by ECA actions.
	/// Implementations are serialized inline via [SerializeReference] with [SubclassSelector] for Inspector support.
	/// Allows actions to derive their integer values from constants, random ranges, character stats, or other sources.
	/// </summary>
	public interface IIntValueProvider
	{
		/// <summary>
		/// Computes and returns the integer value for the current execution context.
		/// </summary>
		/// <param name="initiator">The character initiating the action (used for stat lookups, etc.).</param>
		/// <param name="eventData">The event data for the current execution (used for RNG, target info, etc.).</param>
		/// <returns>The computed integer value.</returns>
		int GetValue(ICharacter initiator, EventData eventData);
	}
}