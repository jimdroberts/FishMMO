using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selects the context <see cref="GameObject"/> itself as the only target.
	/// Useful for self-targeted abilities or effects.
	/// </summary>
	[CreateAssetMenu(fileName = "SelfTargetSelector", menuName = "FishMMO/TargetSelectors/Self", order = 0)]
	public class SelfTargetSelector : TargetSelector
	{
		/// <summary>
		/// Returns the context <see cref="GameObject"/> as the only target, if not null.
		/// </summary>
		/// <param name="context">The <see cref="GameObject"/> to select as the target.</param>
		/// <returns>An enumerable containing only the context object, or empty if null.</returns>
		public override IEnumerable<GameObject> SelectTargets(GameObject context)
		{
			if (context != null)
			{
				var initiator = context.GetComponent<ICharacter>();
				if (AreConditionsMet(context, initiator))
					yield return context;
			}
		}
	}
}