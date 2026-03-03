using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Value provider that returns a random integer between <see cref="Min"/> and <see cref="Max"/> (inclusive).
	/// Uses the deterministic <see cref="System.Random"/> from <see cref="CharacterHitEventData.RNG"/> when available,
	/// otherwise falls back to <see cref="UnityEngine.Random"/>.
	/// </summary>
	[Serializable]
	public sealed class RandomRangeValue : IIntValueProvider
	{
		/// <summary>
		/// The minimum value (inclusive).
		/// </summary>
		[Tooltip("The minimum value (inclusive).")]
		public int Min;

		/// <summary>
		/// The maximum value (inclusive).
		/// </summary>
		[Tooltip("The maximum value (inclusive).")]
		public int Max = 1;

		/// <inheritdoc/>
		public int GetValue(ICharacter initiator, EventData eventData)
		{
			int min = Min;
			int max = Max;

			// Ensure min <= max.
			if (min > max)
			{
				int temp = min;
				min = max;
				max = temp;
			}

			// Use deterministic RNG from CharacterHitEventData when available (server-authoritative).
			if (eventData != null &&
				eventData.TryGet(out CharacterHitEventData hitData) &&
				hitData.RNG != null)
			{
				return hitData.RNG.Next(min, max + 1);
			}

			// Fallback to Unity random.
			return UnityEngine.Random.Range(min, max + 1);
		}
	}
}