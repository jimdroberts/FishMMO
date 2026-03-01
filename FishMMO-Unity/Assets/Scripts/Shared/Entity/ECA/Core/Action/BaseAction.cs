using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for all ECA actions. Serialized inline via [SerializeReference] on Trigger assets.
	/// Derive from this class and add [Serializable] to create concrete actions.
	/// </summary>
	[Serializable]
	public abstract class BaseAction : IAction
	{
		/// <summary>
		/// Executes the action. Must be implemented by derived classes.
		/// </summary>
		/// <param name="initiator">The character initiating the action.</param>
		/// <param name="eventData">Event data for the action.</param>
		public abstract void Execute(ICharacter initiator, EventData eventData);

		/// <summary>
		/// Resolves the target character from event data, falling back to the initiator.
		/// Checks <see cref="CharacterHitEventData"/> for a target override.
		/// </summary>
		/// <param name="initiator">The initiating character (fallback).</param>
		/// <param name="eventData">Optional event data containing a target override.</param>
		/// <returns>The resolved character, or the initiator if no target override is found.</returns>
		protected static ICharacter ResolveTarget(ICharacter initiator, EventData eventData)
		{
			if (eventData != null &&
				eventData.TryGet(out CharacterHitEventData charTargetEventData) &&
				charTargetEventData.Target != null)
			{
				return charTargetEventData.Target;
			}
			return initiator;
		}
	}
}