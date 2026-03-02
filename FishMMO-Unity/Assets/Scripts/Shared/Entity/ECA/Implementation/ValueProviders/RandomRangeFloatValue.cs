using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Float value provider that returns a random float between <see cref="Min"/> and <see cref="Max"/> (inclusive).
	/// Uses the deterministic <see cref="System.Random"/> from <see cref="CharacterHitEventData.RNG"/> when available,
	/// otherwise falls back to <see cref="UnityEngine.Random"/>.
	/// </summary>
	[Serializable]
	public sealed class RandomRangeFloatValue : IFloatValueProvider
	{
		/// <summary>
		/// The minimum value (inclusive).
		/// </summary>
		[Tooltip("The minimum value (inclusive).")]
		public float Min;

		/// <summary>
		/// The maximum value (inclusive).
		/// </summary>
		[Tooltip("The maximum value (inclusive).")]
		public float Max = 1f;

		/// <inheritdoc/>
		public float GetValue(ICharacter initiator, EventData eventData)
		{
			float min = Min;
			float max = Max;

			// Ensure min <= max.
			if (min > max)
			{
				float temp = min;
				min = max;
				max = temp;
			}

			// Use deterministic RNG from CharacterHitEventData when available (server-authoritative).
			if (eventData != null &&
				eventData.TryGet(out CharacterHitEventData hitData) &&
				hitData.RNG != null)
			{
				// NextDouble returns [0.0, 1.0), scale to [min, max].
				return (float)(min + (hitData.RNG.NextDouble() * (max - min)));
			}

			// Fallback to Unity random.
			return UnityEngine.Random.Range(min, max);
		}
	}
}