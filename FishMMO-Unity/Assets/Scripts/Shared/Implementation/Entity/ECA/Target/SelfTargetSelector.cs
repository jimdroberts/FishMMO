using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selects the context <see cref="GameObject"/> itself as the only target.
	/// Useful for self-targeted abilities or effects.
	/// </summary>
	[Serializable]
	public class SelfTargetSelector : TargetSelector
	{
		/// <summary>
		/// Returns the context <see cref="GameObject"/> as the only target, if not null.
		/// </summary>
		/// <param name="context">The <see cref="GameObject"/> to select as the target.</param>
		/// <returns>An enumerable containing only the context object, or empty if null.</returns>
		public override IEnumerable<GameObject> SelectTargets(GameObject context)
		{
			if (TrySelectTargetOverride(context, out GameObject overrideTarget))
			{
				if (overrideTarget != null)
				{
					yield return overrideTarget;
				}
				yield break;
			}

			if (context != null)
			{
				ICharacter initiator = ResolveInitiator(context);
				if (AreConditionsMet(context, initiator))
					yield return context;
			}
		}
	}
}