using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Character provider that returns the target from <see cref="CharacterHitEventData"/>.
	/// Optionally falls back to the initiator when no event target is available.
	/// </summary>
	[Serializable]
	public sealed class EventTargetCharacterProvider : ICharacterProvider
	{
		/// <summary>
		/// If true, falls back to the initiator when no event target is available.
		/// If false, returns null when no event target is present.
		/// </summary>
		[Tooltip("If true, falls back to the initiator when no event target is available.")]
		public bool FallbackToInitiator = true;

		/// <inheritdoc/>
		public ICharacter GetCharacter(ICharacter initiator, EventData eventData)
		{
			if (eventData != null &&
				eventData.TryGet(out CharacterHitEventData hitData) &&
				hitData.Target != null)
			{
				return hitData.Target;
			}
			return FallbackToInitiator ? initiator : null;
		}
	}
}