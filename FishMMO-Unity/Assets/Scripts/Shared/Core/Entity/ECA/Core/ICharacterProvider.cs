namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Interface for polymorphic character providers used by ECA actions and conditions.
	/// Implementations are serialized inline via [SerializeReference] with [SubclassSelector] for Inspector support.
	/// Allows actions and conditions to resolve their target character from various sources.
	/// </summary>
	public interface ICharacterProvider
	{
		/// <summary>
		/// Resolves and returns the character for the current execution context.
		/// </summary>
		/// <param name="initiator">The character initiating the action or condition.</param>
		/// <param name="eventData">The event data for the current execution.</param>
		/// <returns>The resolved character, or null if no character could be resolved.</returns>
		ICharacter GetCharacter(ICharacter initiator, EventData eventData);
	}
}