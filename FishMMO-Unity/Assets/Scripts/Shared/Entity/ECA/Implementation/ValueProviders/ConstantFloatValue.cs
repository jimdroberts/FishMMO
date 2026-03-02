using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Float value provider that always returns a fixed constant value.
	/// </summary>
	[Serializable]
	public sealed class ConstantFloatValue : IFloatValueProvider
	{
		/// <summary>
		/// The fixed float value to return.
		/// </summary>
		[Tooltip("The fixed constant float value.")]
		public float Amount;

		/// <inheritdoc/>
		public float GetValue(ICharacter initiator, EventData eventData)
		{
			return Amount;
		}
	}
}