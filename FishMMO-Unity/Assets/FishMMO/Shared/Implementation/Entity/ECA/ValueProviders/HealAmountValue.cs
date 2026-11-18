using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Value provider that reads the heal amount from <see cref="HealEventData"/>.
	/// Returns a configurable fallback when no heal event data is present.
	/// </summary>
	[Serializable]
	public sealed class HealAmountValue : IIntValueProvider
	{
		/// <summary>
		/// The fallback value returned when no <see cref="HealEventData"/> is present.
		/// </summary>
		[Tooltip("Fallback value when HealEventData is not present.")]
		public int Fallback = 0;

		/// <inheritdoc/>
		/// <summary>
		/// Nothing to say ahead of time: this reads the healing that landed from the event being handled.
		/// </summary>
		/// <inheritdoc/>
		public string Describe() => null;

		/// <inheritdoc/>
		public int GetValue(ICharacter initiator, EventData eventData)
		{
			if (eventData != null && eventData.TryGet(out HealEventData healData))
			{
				return healData.Amount;
			}
			return Fallback;
		}
	}
}