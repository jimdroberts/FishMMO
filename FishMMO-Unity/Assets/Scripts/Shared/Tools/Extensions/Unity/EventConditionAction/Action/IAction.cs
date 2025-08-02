namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for executable actions in FishMMO. Defines the contract for action execution.
	/// </summary>
	public interface IAction
	{
		/// <summary>
		/// Executes the action with the given initiator and event data.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">Event data for the action.</param>
		void Execute(ICharacter initiator, EventData eventData);
	}
}