using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selects all direct children of the context <see cref="GameObject"/>.
	/// Useful for applying effects to all immediate child objects.
	/// </summary>
	[Serializable]
	public class ChildrenTargetSelector : TargetSelector
	{
		/// <summary>
		/// Returns all direct children of the context <see cref="GameObject"/>.
		/// </summary>
		/// <param name="eventData">The event driving the selection; its context object is the parent.</param>
		/// <returns>An enumerable of all direct child <see cref="GameObject"/>s, or empty if there is no context.</returns>
		public override IEnumerable<GameObject> SelectTargets(EventData eventData)
		{
			GameObject context = GetContext(eventData);
			if (context == null) yield break;
			Transform contextTransform = context.transform;
			for (int i = 0; i < contextTransform.childCount; i++)
			{
				Transform child = contextTransform.GetChild(i);
				if (AreConditionsMet(child.gameObject, eventData))
					yield return child.gameObject;
			}
		}
	}
}