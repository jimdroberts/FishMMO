using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selects all direct children of the context <see cref="GameObject"/>.
	/// Useful for applying effects to all immediate child objects.
	/// </summary>
	[CreateAssetMenu(fileName = "ChildrenTargetSelector", menuName = "FishMMO/TargetSelectors/Children", order = 3)]
	public class ChildrenTargetSelector : TargetSelector
	{
		/// <summary>
		/// Returns all direct children of the context <see cref="GameObject"/>.
		/// </summary>
		/// <param name="context">The parent <see cref="GameObject"/> whose children to select.</param>
		/// <returns>An enumerable of all direct child <see cref="GameObject"/>s, or empty if context is null.</returns>
		public override IEnumerable<GameObject> SelectTargets(GameObject context)
		{
			if (context == null) yield break;
			var initiator = context != null ? context.GetComponent<ICharacter>() : null;
			foreach (Transform child in context.transform)
			{
				if (AreConditionsMet(child.gameObject, initiator))
					yield return child.gameObject;
			}
		}
	}
}