using System;
using UnityEngine;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Abstract base class for all ECA conditions. Serialized inline via [SerializeReference] on Trigger assets.
	/// Derive from this class and add [Serializable] to create concrete conditions.
	/// </summary>
	[Serializable]
	public abstract class BaseCondition : ICondition
	{
		/// <summary>
		/// Optional provider that determines how this condition resolves its target character.
		/// When null, falls back to event target if available, otherwise the initiator.
		/// </summary>
		[Tooltip("How this condition resolves its target character. When unset, uses event target or initiator.")]
		[SerializeReference, SubclassSelector]
		public ICharacterProvider TargetProvider;

		/// <summary>
		/// Evaluates the condition. Must be implemented by derived classes.
		/// </summary>
		/// <param name="initiator">The character initiating the check.</param>
		/// <param name="eventData">Optional event data for the condition.</param>
		/// <returns>True if the condition is met; otherwise, false.</returns>
		public abstract bool Evaluate(ICharacter initiator, EventData eventData = null);

		/// <summary>
		/// Resolves the target character using <see cref="TargetProvider"/> if set,
		/// otherwise falls back to <see cref="CharacterHitEventData.Target"/> or the initiator.
		/// </summary>
		/// <param name="initiator">The initiating character (fallback).</param>
		/// <param name="eventData">Optional event data containing a target override.</param>
		/// <returns>The resolved character, or the initiator if no target override is found.</returns>
		protected ICharacter ResolveTarget(ICharacter initiator, EventData eventData)
		{
			if (TargetProvider != null)
			{
				return TargetProvider.GetCharacter(initiator, eventData);
			}
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