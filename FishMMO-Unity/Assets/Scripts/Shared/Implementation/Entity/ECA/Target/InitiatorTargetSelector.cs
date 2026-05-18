using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selects <see cref="EventData.Initiator"/>'s GameObject as the only target. Useful for
	/// self-targeted effects regardless of any current event target.
	/// </summary>
	[Serializable]
	public class InitiatorTargetSelector : TargetSelector
	{
		/// <summary>
		/// Yields the event initiator's GameObject when present.
		/// </summary>
		/// <param name="eventData">The event data driving the selection.</param>
		/// <returns>An enumerable containing only the initiator's GameObject, or empty when none.</returns>
		public override IEnumerable<GameObject> SelectTargets(EventData eventData)
		{
			GameObject initiatorGameObject = eventData?.Initiator?.GameObject;
			if (initiatorGameObject != null && AreConditionsMet(initiatorGameObject, eventData))
			{
				yield return initiatorGameObject;
			}
		}
	}
}