#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Lists every loaded GameObject carrying a component whose script cannot be found — the objects behind
	/// Unity's "The referenced script (Unknown) on this Behaviour is missing!" — including hidden and
	/// don't-save objects that no scene file shows.
	/// </summary>
	/// <remarks>
	/// That warning names nothing. It is logged on a domain reload for every loaded object whose script GUID
	/// no longer resolves, and the object can be in an open scene, a prefab being edited, or held in memory
	/// with HideFlags by an editor tool, where neither the hierarchy nor a scan of the files on disk finds it.
	/// </remarks>
	public static class MissingScriptFinder
	{
		[DashboardTool(DashboardToolAttribute.Validate, "Find missing scripts on loaded objects", Section = "Scenes", Order = 50,
			Tooltip = "Logs every loaded GameObject (scenes, prefab stage, hidden editor objects) with a component whose script is missing, with its scene, path and hide flags. Changes nothing.")]
		public static void Find()
		{
			var report = new StringBuilder();
			int objects = 0, components = 0;
			foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
			{
				if (go == null || EditorUtility.IsPersistent(go) && !PrefabUtility.IsPartOfPrefabAsset(go))
				{
					continue;
				}
				int missing = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
				if (missing == 0)
				{
					continue;
				}
				objects++;
				components += missing;
				string where = go.scene.IsValid() ? go.scene.path : EditorUtility.IsPersistent(go) ? AssetDatabase.GetAssetPath(go) : "(no scene)";
				report.Append("\n  ").Append(missing).Append(" missing on '").Append(PathOf(go.transform))
					.Append("' in ").Append(where).Append(" hideFlags=").Append(go.hideFlags);
			}
			Debug.Log(objects == 0
				? "[Missing scripts] No loaded object has a missing script."
				: $"[Missing scripts] {components} missing script(s) on {objects} object(s):{report}");
		}

		private static string PathOf(Transform t)
		{
			var path = new StringBuilder(t.name);
			for (Transform p = t.parent; p != null; p = p.parent)
			{
				path.Insert(0, p.name + "/");
			}
			return path.ToString();
		}
	}
}
#endif
