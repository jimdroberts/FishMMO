using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Selects every scene <see cref="GameObject"/> with a component that implements <see cref="ICharacter"/>.
	/// </summary>
	[Serializable]
	public class AllCharactersTargetSelector : TargetSelector
	{
		/// <summary>
		/// True to include inactive GameObjects when collecting scene characters.
		/// </summary>
		[Tooltip("True to include inactive GameObjects when collecting scene characters.")]
		public bool IncludeInactive;

		/// <summary>
		/// True to include characters that are not currently spawned.
		/// </summary>
		[Tooltip("True to include characters that are not currently spawned.")]
		public bool IncludeUnspawned;

		/// <summary>
		/// Returns all GameObjects in the active scene that have a component implementing <see cref="ICharacter"/>.
		/// </summary>
		/// <param name="context">The context GameObject used to identify the scene.</param>
		/// <returns>An enumerable of character GameObjects.</returns>
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

			if (context == null)
			{
				yield break;
			}

			ICharacter initiator = ResolveInitiator(context);

			MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
				IncludeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
				FindObjectsSortMode.None);

			for (int i = 0; i < behaviours.Length; i++)
			{
				MonoBehaviour behaviour = behaviours[i];
				if (behaviour == null || behaviour.gameObject.scene != context.scene)
				{
					continue;
				}

				if (behaviour is ICharacter character &&
					(IncludeUnspawned || character.IsSpawned) &&
					AreConditionsMet(character.GameObject, initiator))
				{
					yield return character.GameObject;
				}
			}
		}
	}
}