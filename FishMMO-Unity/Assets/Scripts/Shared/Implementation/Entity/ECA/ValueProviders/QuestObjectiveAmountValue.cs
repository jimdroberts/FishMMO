using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Value provider that reads the objective amount from <see cref="QuestEventData"/>.
	/// Returns a configurable fallback when no quest event data is present.
	/// </summary>
	[Serializable]
	public sealed class QuestObjectiveAmountValue : IIntValueProvider
	{
		/// <summary>
		/// The fallback value returned when no <see cref="QuestEventData"/> is present.
		/// </summary>
		[Tooltip("Fallback value when QuestEventData is not present.")]
		public int Fallback = 0;

		/// <inheritdoc/>
		/// <summary>
		/// Nothing to say ahead of time: this reads the objective amount from the event from the event being handled.
		/// </summary>
		/// <inheritdoc/>
		public string Describe() => null;

		/// <inheritdoc/>
		public int GetValue(ICharacter initiator, EventData eventData)
		{
			if (eventData != null && eventData.TryGet(out QuestEventData questData))
			{
				return (int)questData.Amount;
			}
			return Fallback;
		}
	}
}
