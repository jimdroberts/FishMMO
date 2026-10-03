#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Switches Unity's own tree drawing off and on again on every terrain in the open scenes, to see
	/// what the tree channel costs in draw calls.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Unity's terrain draws tree-channel prefabs that carry a LODGroup one instance at a time (and once
	/// more per shadow cascade), whatever their materials' instancing setting; the Statistics window shows
	/// them as "Non-SRP Compatible", one instance a call. Setting <see cref="Terrain.treeDistance"/> to 0
	/// stops that drawing and nothing else: tree colliders belong to the TerrainCollider, and details keep
	/// their own distance. The drop in draw calls is what the tree channel was costing.
	/// </para>
	/// <para>
	/// <b>A diagnostic, not a setting.</b> The change is made without Undo and without dirtying the scene,
	/// and the second press puts back each terrain's own distance. A scene saved while it is off would keep
	/// a tree distance of 0 if anything else had dirtied it, so press it again before saving.
	/// </para>
	/// </remarks>
	public static class TerrainTreeDrawToggle
	{
		/// <summary>
		/// Each switched-off terrain's own tree distance, to put back, keyed by its GlobalObjectId and kept in
		/// <see cref="SessionState"/>.
		/// </summary>
		/// <remarks>
		/// <b>Not a static field.</b> It was one, and a recompile between the two presses (new scripts arriving,
		/// any edit) cleared it: the second press then found every terrain at 0, said none drew trees, and left
		/// the scene treeless with nothing to restore from. SessionState survives domain reloads for the life of
		/// the editor.
		/// </remarks>
		private const string StateKey = "FishMMO.TerrainTreeDrawToggle.Saved";

		[DashboardTool(DashboardToolAttribute.Biomes, "Toggle terrain tree drawing (draw-call check)", Section = "Diagnostics", Order = 100,
			Tooltip = "Turns Unity's terrain tree drawing off on every terrain in the open scenes (Tree Distance 0), or back to each terrain's own distance. Colliders and details are untouched. Compare the Statistics window's draw calls before and after; press again before saving the scene.")]
		public static void Toggle()
		{
			Dictionary<string, float> saved = Load();
			if (saved.Count > 0)
			{
				int restored = 0;
				foreach (Terrain terrain in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
				{
					// A terrain the record does not name was not switched off here, or its scene was reloaded
					// (which already put its own distance back).
					if (terrain != null && saved.TryGetValue(IdOf(terrain), out float distance))
					{
						terrain.treeDistance = distance;
						restored++;
					}
				}
				SessionState.EraseString(StateKey);
				SceneView.RepaintAll();
				Debug.Log($"[Terrain trees] Tree drawing back on for {restored} terrain(s), at their own distances.");
				return;
			}

			int switched = 0;
			foreach (Terrain terrain in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
			{
				if (terrain == null || terrain.treeDistance <= 0f)
				{
					continue;
				}
				saved[IdOf(terrain)] = terrain.treeDistance;
				terrain.treeDistance = 0f;
				switched++;
			}
			Store(saved);
			SceneView.RepaintAll();
			Debug.Log(switched > 0
				? $"[Terrain trees] Tree drawing OFF on {switched} terrain(s). Compare the Statistics window's draw calls, then press the button again before saving the scene."
				: "[Terrain trees] No terrain in the open scenes draws trees. If they were switched off before this fix, reopen the scene without saving to get their distances back.");
		}

		private static string IdOf(Terrain terrain) => GlobalObjectId.GetGlobalObjectIdSlow(terrain).ToString();

		/// <summary>The record, one "id=distance" per line.</summary>
		private static Dictionary<string, float> Load()
		{
			var saved = new Dictionary<string, float>();
			foreach (string line in SessionState.GetString(StateKey, string.Empty).Split('\n'))
			{
				int split = line.LastIndexOf('=');
				if (split > 0 && float.TryParse(line.Substring(split + 1), System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture, out float distance))
				{
					saved[line.Substring(0, split)] = distance;
				}
			}
			return saved;
		}

		private static void Store(Dictionary<string, float> saved)
		{
			var text = new System.Text.StringBuilder();
			foreach (KeyValuePair<string, float> pair in saved)
			{
				text.Append(pair.Key).Append('=').Append(pair.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
			}
			SessionState.SetString(StateKey, text.ToString());
		}
	}
}
#endif
