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
		/// <b>Server only, and the one spatial-family selector that stayed that way</b> when the
		/// rest were opened up for client-side prediction. The others ask "which bodies are inside
		/// this volume", a question the caster's client can answer for itself because its live world
		/// is the same world the server rewinds to. This one asks "who is in the ZONE", and a client
		/// simply does not hold that set: observer streaming gives it only the characters currently
		/// spawned for that viewer, so predicting here would not be a boundary mispick but a
		/// systematic under-selection — every culled character silently missing from an effect that
		/// is defined as hitting everyone. It is also the only selector that pays a full-scene
		/// component scan, which does not belong on a player's machine once per tick.
		/// </para>
		/// <para>
		/// The consequence is honest and deliberate: a zone-wide effect has no predicted feedback and
		/// lands when the server says so. See <see cref="TargetSelector.ResolvesTargetsLocally"/>.
		/// </para>
		/// <para>
		/// <b>Ordered.</b> <c>FindObjectsByType</c> is called with <c>FindObjectsSortMode.None</c>,
		/// which returns whatever order the scene's object registry happens to hold — not the
		/// hierarchy order, and not the same order twice. Anything downstream that caps the set,
		/// stops at the first match, or rolls against it was choosing arbitrarily. Sorting by network
		/// identity costs one pass and makes the fan-out reproducible.
		/// </para>
		/// </remarks>
		/// <para>
		/// <b>Rewound, despite selecting no volume.</b> Which characters this yields does not depend
		/// on where anybody is — but its <see cref="TargetSelector.Conditions"/> are designer
		/// authored and may be positional (<see cref="WithinRangeCondition"/>,
		/// <see cref="HasLineOfSightCondition"/>, <see cref="IsWithinFacingAngleCondition"/>). A
		/// condition evaluated outside the scope would filter this fan-out by where the SERVER holds
		/// its characters while every other selector filters by where the caster saw them, so the
		/// same authored range would mean two different things depending on which selector produced
		/// the candidate. Running the whole gather under one scope makes the answer uniform; when
		/// there is nothing to compensate, <see cref="TargetSelector.GatherRewound"/> simply runs the
		/// body directly.
		/// </para>
		/// <param name="eventData">The event data driving the selection.</param>
		/// <returns>An enumerable of character GameObjects, ordered by network identity.</returns>
		public override IEnumerable<GameObject> SelectTargets(EventData eventData)
		{
			// Server only — see the remarks above; a client does not hold the zone's character set.
			if (!IsAuthoritativePeer(eventData))
			{
				yield break;
			}

			GameObject context = GetContext(eventData);
			if (context == null)
			{
				yield break;
			}

			List<GameObject> results = new List<GameObject>();
			GatherRewound(eventData, context, results, Gather);

			for (int i = 0; i < results.Count; ++i)
			{
				yield return results[i];
			}
		}

		/// <summary>
		/// Collects and orders every character in the context's scene. Runs inside the rewind scope.
		/// </summary>
		/// <remarks>
		/// An instance method rather than a static one so it can reach
		/// <see cref="TargetSelector.AreConditionsMet"/>; the delegate is allocated per selection,
		/// which is the same cost the enumerator itself already carries.
		/// </remarks>
		private void Gather(EventData eventData, GameObject context, List<GameObject> results)
		{
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
				results.Add(candidates[ranks[i].Index]);
			}
		}
	}
}
