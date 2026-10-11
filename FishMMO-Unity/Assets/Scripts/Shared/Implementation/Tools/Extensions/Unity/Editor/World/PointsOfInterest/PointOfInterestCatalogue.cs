#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// An optional asset that replaces rows of the code catalogue (<see cref="PointOfInterestRules.Defaults"/>) for
	/// every scene cut in this project.
	/// </summary>
	/// <remarks>
	/// None is needed: with no asset the defaults are used. With one, each row it holds replaces the default row of
	/// its kind and every other kind keeps its default, so an asset made today does not freeze tomorrow's defaults.
	/// The first one found (by path, so the choice is stable) is used.
	/// </remarks>
	[CreateAssetMenu(fileName = "Points of Interest Catalogue", menuName = "FishMMO/World/Points of Interest Catalogue")]
	public class PointOfInterestCatalogue : ScriptableObject
	{
		public List<PointOfInterestKindRule> Overrides = new List<PointOfInterestKindRule>();

		/// <summary>The rules every cut uses: the defaults, with the project's catalogue asset laid over them when it has one.</summary>
		public static PointOfInterestRules Resolve()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			PointOfInterestCatalogue asset = Find();
			return asset != null ? rules.With(asset.Overrides) : rules;
		}

		/// <summary>The project's catalogue asset, or null.</summary>
		public static PointOfInterestCatalogue Find()
		{
			var paths = new List<string>();
			foreach (string guid in AssetDatabase.FindAssets($"t:{nameof(PointOfInterestCatalogue)}"))
			{
				paths.Add(AssetDatabase.GUIDToAssetPath(guid));
			}
			paths.Sort(System.StringComparer.Ordinal);
			return paths.Count > 0 ? AssetDatabase.LoadAssetAtPath<PointOfInterestCatalogue>(paths[0]) : null;
		}

		/// <summary>Fills the asset with the default rows, to edit from.</summary>
		[ContextMenu("Fill with the defaults")]
		private void FillWithDefaults()
		{
			Undo.RecordObject(this, "Fill with the defaults");
			Overrides = PointOfInterestRules.Defaults();
			EditorUtility.SetDirty(this);
		}
	}
}
#endif
