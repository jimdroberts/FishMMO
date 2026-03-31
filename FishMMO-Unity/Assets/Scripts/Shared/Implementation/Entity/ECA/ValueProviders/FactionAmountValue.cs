using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Value provider that reads the faction value from <see cref="FactionEventData"/>.
	/// Returns a configurable fallback when no faction event data is present.
	/// </summary>
	[Serializable]
	public sealed class FactionAmountValue : IIntValueProvider
	{
		/// <summary>
		/// The fallback value returned when no <see cref="FactionEventData"/> is present.
		/// </summary>
		[Tooltip("Fallback value when FactionEventData is not present.")]
		public int Fallback = 0;

		/// <inheritdoc/>
		public int GetValue(ICharacter initiator, EventData eventData)
		{
			if (eventData != null && eventData.TryGet(out FactionEventData factionData))
			{
				return factionData.Value;
			}
			return Fallback;
		}
	}
}