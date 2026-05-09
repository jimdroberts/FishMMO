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
		/// <param name="context">The parent <see cref="GameObject"/> whose children to select.</param>
		/// <returns>An enumerable of all direct child <see cref="GameObject"/>s, or empty if context is null.</returns>
		public override IEnumerable<GameObject> SelectTargets(EventData eventData)
		{
			if (TrySelectTargetOverride(eventData, out GameObject overrideTarget))
			{
				if (overrideTarget != null)
				{
					yield return overrideTarget;
				}
				yield break;
			}

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