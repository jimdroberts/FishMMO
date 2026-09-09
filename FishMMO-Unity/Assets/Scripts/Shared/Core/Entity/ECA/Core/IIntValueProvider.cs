namespace FishMMO.Shared.Core
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

		/// <summary>
		/// Describes the value in words, without a character or an event to compute it against.
		/// </summary>
		/// <remarks>
		/// A tooltip is written long before anything is cast, so it cannot call
		/// <see cref="GetValue"/> — there is no initiator, no target and no RNG stream. A provider
		/// that knows its own numbers can still say what they are ("18 - 24"), and one that cannot
		/// returns null so the caller writes nothing rather than a placeholder.
		/// <para>
		/// On the interface rather than in a switch statement somewhere in the UI, because a new
		/// provider should have to answer this to compile.
		/// </para>
		/// </remarks>
		/// <returns>The description, or null when the value is only knowable at execution time.</returns>
		string Describe();
	}
}