using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Float value provider that returns a random float between <see cref="Min"/> and <see cref="Max"/> (inclusive).
	/// Uses the deterministic <see cref="DeterministicRNG"/> from <see cref="EventData.RNG"/> when available,
	/// otherwise falls back to <see cref="DeterministicRNG.Shared"/>.
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

			// Use deterministic RNG from EventData when available (server-authoritative).
			if (eventData != null && eventData.RNG != null)
			{
				return eventData.RNG.Range(min, max);
			}

			// Fallback to shared RNG.
			return DeterministicRNG.Shared.Range(min, max);
		}
	}
}