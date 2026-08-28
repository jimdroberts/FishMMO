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
		/// Returns all GameObjects in the context's scene that have a component implementing <see cref="ICharacter"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Server only.</b> This feeds gameplay — it is the fan-out behind "everyone in the zone"
		/// effects — so it resolves where those effects are authoritative, for the same reason the
		/// physics selectors do.
		/// </para>
		/// <para>
		/// <b>Ordered.</b> <c>FindObjectsByType</c> is called with <c>FindObjectsSortMode.None</c>,
		/// which returns whatever order the scene's object registry happens to hold — not the
		/// hierarchy order, and not the same order twice. Anything downstream that caps the set,
		/// stops at the first match, or rolls against it was choosing arbitrarily. Sorting by network
		/// identity costs one pass and makes the fan-out reproducible.
		/// </para>
		/// </remarks>
		/// <param name="eventData">The event data driving the selection.</param>
		/// <returns>An enumerable of character GameObjects, ordered by network identity.</returns>
		public override IEnumerable<GameObject> SelectTargets(EventData eventData)
		{
			if (!IsAuthoritativePeer(eventData))
			{
				yield break;
			}

			GameObject context = GetContext(eventData);
			if (context == null)
			{
				yield break;
			}

			MonoBehaviour[] behaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
				IncludeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
				FindObjectsSortMode.None);

			List<GameObject> candidates = new List<GameObject>();
			List<TargetRank> ranks = new List<TargetRank>();

			for (int i = 0; i < behaviours.Length; i++)
			{
				MonoBehaviour behaviour = behaviours[i];
				if (behaviour == null || behaviour.gameObject.scene != context.scene)
				{
					continue;
				}

				if (behaviour is ICharacter character &&
					(IncludeUnspawned || character.IsSpawned) &&
					AreConditionsMet(character.GameObject, eventData))
				{
					candidates.Add(character.GameObject);
					ranks.Add(TargetOrdering.Rank(candidates.Count - 1, character.GameObject, 0f));
				}
			}

			TargetOrdering.SortStable(ranks);

			for (int i = 0; i < ranks.Count; ++i)
			{
				yield return candidates[ranks[i].Index];
			}
		}
	}
}
