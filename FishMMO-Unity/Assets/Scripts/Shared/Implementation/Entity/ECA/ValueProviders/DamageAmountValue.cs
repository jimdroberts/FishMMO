using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Value provider that reads the damage amount from <see cref="DamageEventData"/>.
	/// Returns a configurable fallback when no damage event data is present.
	/// </summary>
	[Serializable]
	public sealed class DamageAmountValue : IIntValueProvider
	{
		/// <summary>
		/// The fallback value returned when no <see cref="DamageEventData"/> is present.
		/// </summary>
		[Tooltip("Fallback value when DamageEventData is not present.")]
		public int Fallback = 0;

		/// <inheritdoc/>
		public int GetValue(ICharacter initiator, EventData eventData)
		{
			if (eventData != null && eventData.TryGet(out DamageEventData damageData))
			{
				return damageData.Amount;
			}
			return Fallback;
		}
	}
}