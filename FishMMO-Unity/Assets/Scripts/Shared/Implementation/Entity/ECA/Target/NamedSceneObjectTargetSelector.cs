using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// Resolves a scene <see cref="GameObject"/> by name at runtime. Designed for asset-based
	/// Triggers that need to point at a specific scene object — selectors serialized on
	/// ScriptableObject assets cannot hold direct scene references, but they can hold a name
	/// string and look the object up when the trigger fires.
	/// <para>
	/// The lookup is scoped to the context's scene (whatever scene the event's Target or
	/// Initiator lives in). If no scene context is available, the active scene is used.
	/// Names are compared exactly (case sensitive). For non-unique names, use
	/// <see cref="TaggedSceneObjectTargetSelector"/> instead, or compose this selector with
	/// per-target conditions to disambiguate.
	/// </para>
	/// <para>
	/// <b>Performance:</b> the lookup walks the scene's root objects and their descendants on
	/// every fire. For frequent triggers, cache the resolved GameObject in your scene wiring
	/// rather than depending on a name lookup.
	/// </para>
	/// </summary>
	[Serializable]
	public class NamedSceneObjectTargetSelector : TargetSelector
	{
		/// <summary>
		/// The name of the GameObject to find in the scene. Compared exactly.
		/// </summary>
		[Tooltip("Name of the GameObject to find in the current scene.")]
		public string ObjectName;

		/// <summary>
		/// When true, also searches inactive GameObjects in the scene hierarchy.
		/// </summary>
		[Tooltip("Include inactive GameObjects in the scene scan.")]
		public bool IncludeInactive;

		/// <inheritdoc/>
		public override IEnumerable<GameObject> SelectTargets(EventData eventData)
		{
			if (string.IsNullOrEmpty(ObjectName))
			{
				Log.Warning("NamedSceneObjectTargetSelector", "ObjectName is empty; nothing to resolve.");
				yield break;
			}

			GameObject context = GetContext(eventData);
			Scene scene = (context != null && context.scene.IsValid())
				? context.scene
				: SceneManager.GetActiveScene();

			if (!scene.IsValid())
			{
				yield break;
			}

			GameObject[] roots = scene.GetRootGameObjects();
			for (int i = 0; i < roots.Length; ++i)
			{
				GameObject found = FindByName(roots[i].transform, ObjectName, IncludeInactive);
				if (found != null && AreConditionsMet(found, eventData))
				{
					yield return found;
					yield break;
				}
			}
		}

		private static GameObject FindByName(Transform root, string name, bool includeInactive)
		{
			if (root == null) return null;
			if ((includeInactive || root.gameObject.activeInHierarchy) && root.name == name)
			{
				return root.gameObject;
			}
			for (int i = 0; i < root.childCount; ++i)
			{
				GameObject hit = FindByName(root.GetChild(i), name, includeInactive);
				if (hit != null) return hit;
			}
			return null;
		}
	}
}
