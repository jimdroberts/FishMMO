using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for all ECA conditions. Serialized inline via [SerializeReference] on Trigger assets.
	/// Derive from this class and add [Serializable] to create concrete conditions.
	/// </summary>
	[Serializable]
	public abstract class BaseCondition : ICondition
	{
		/// <summary>
		/// Evaluates the condition. Must be implemented by derived classes.
		/// </summary>
		/// <param name="initiator">The character initiating the check.</param>
		/// <param name="eventData">Optional event data for the condition.</param>
		/// <returns>True if the condition is met; otherwise, false.</returns>
		public abstract bool Evaluate(ICharacter initiator, EventData eventData = null);

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