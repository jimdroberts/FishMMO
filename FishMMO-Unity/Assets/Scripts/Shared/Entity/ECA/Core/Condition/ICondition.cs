namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for condition evaluation in FishMMO. Defines the contract for condition checks.
	/// </summary>
	public interface ICondition
	{
		/// <summary>
		/// Evaluates the condition with the given initiator and event data.
		/// </summary>
		/// <param name="initiator">The character initiating the check.</param>
		/// <param name="eventData">Event data for the condition.</param>
		/// <returns>True if the condition is met; otherwise, false.</returns>
		bool Evaluate(ICharacter initiator, EventData eventData);
	}
}