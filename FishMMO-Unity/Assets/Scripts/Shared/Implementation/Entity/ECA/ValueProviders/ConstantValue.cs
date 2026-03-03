using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Value provider that always returns a fixed constant value.
	/// </summary>
	[Serializable]
	public sealed class ConstantValue : IIntValueProvider
	{
		/// <summary>
		/// The fixed value to return.
		/// </summary>
		[Tooltip("The fixed constant value.")]
		public int Amount;

		/// <inheritdoc/>
		public int GetValue(ICharacter initiator, EventData eventData)
		{
			return Amount;
		}
	}
}