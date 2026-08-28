using System;
using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Resolves scene <see cref="GameObject"/>s by Unity tag at runtime. Designed for
	/// asset-based Triggers that need to target one or more pre-tagged scene objects —
	/// selectors serialized on ScriptableObject assets cannot hold direct scene references,
	/// but they can hold a tag string and resolve it at fire time.
	/// <para>
	/// The lookup is scoped to the context's scene (whatever scene the event's Target or
	/// Initiator lives in). Results are filtered through this selector's
	/// <see cref="TargetSelector.Conditions"/>.
	/// </para>
	/// <para>
	/// <b>Tag pre-requisite:</b> the tag must exist in Unity's Tag Manager
	/// (ProjectSettings > Tags and Layers). Using an unknown tag logs a warning and yields
	/// no targets.
	/// </para>
	/// </summary>
	[Serializable]
	public class TaggedSceneObjectTargetSelector : TargetSelector
	{
		/// <summary>
		/// Unity tag to match. Must be defined in the project's Tag Manager.
		/// </summary>
		[Tooltip("Unity tag to match. Must be defined in the project's Tag Manager.")]
		public string Tag;

		/// <summary>
		/// When true, yields only the first matching GameObject; otherwise yields all matches.
		/// </summary>
		[Tooltip("Yield only the first matching GameObject. Otherwise yield all matches.")]
		public bool FirstOnly;

		/// <inheritdoc/>
		public override IEnumerable<GameObject> SelectTargets(EventData eventData)
		{
			if (string.IsNullOrEmpty(Tag))
			{
				Log.Warning("TaggedSceneObjectTargetSelector", "Tag is empty; nothing to resolve.");
				yield break;
			}

			GameObject[] matches;
			try
			{
				matches = GameObject.FindGameObjectsWithTag(Tag);
			}
			catch (UnityException)
			{
				Log.Warning("TaggedSceneObjectTargetSelector", $"Tag '{Tag}' is not defined in the project Tag Manager.");
				yield break;
			}

			GameObject context = GetContext(eventData);
			UnityEngine.SceneManagement.Scene? scene = (context != null && context.scene.IsValid()) ? context.scene : (UnityEngine.SceneManagement.Scene?)null;

			/* Ordered before anything is yielded, because FirstOnly otherwise means "whichever object
			 * FindGameObjectsWithTag listed first" — a peer-dependent and run-dependent answer for
			 * something a designer authored as a specific choice. Sorting by network identity, then by
			 * name for un-networked scene objects (which is what these usually are), makes "the first
			 * one" mean the same object everywhere. */
			List<GameObject> candidates = new List<GameObject>();
			List<TargetRank> ranks = new List<TargetRank>();

			for (int i = 0; i < matches.Length; ++i)
			{
				GameObject candidate = matches[i];
				if (candidate == null) continue;
				if (scene.HasValue && candidate.scene != scene.Value) continue;
				if (!AreConditionsMet(candidate, eventData)) continue;

				candidates.Add(candidate);
				ranks.Add(TargetOrdering.Rank(candidates.Count - 1, candidate, 0f));
			}

			TargetOrdering.SortStable(ranks);

			for (int i = 0; i < ranks.Count; ++i)
			{
				yield return candidates[ranks[i].Index];
				if (FirstOnly) yield break;
			}
		}
	}
}
