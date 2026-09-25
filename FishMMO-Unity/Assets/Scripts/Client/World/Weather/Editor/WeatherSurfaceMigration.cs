using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.Client
{
	/// <summary>
	/// Moves the world's materials onto the weather shaders, so rain wets them and snow settles on
	/// them. Characters and effects are left alone (Q12).
	/// </summary>
	/// <remarks>
	/// <para>
	/// A material keeps every value it had: <c>FishMMO/Weather Lit</c> is URP's Lit with two
	/// properties added, so swapping the shader changes nothing about how a surface looks in fair
	/// weather. A material that should ignore the weather keeps its old shader, or sets
	/// <c>Weather on this surface</c> to 0.
	/// </para>
	/// <para>
	/// Terrain is separate, because Unity's terrains point at the URP package's own
	/// <c>TerrainLit.mat</c> — a file inside a package, which cannot carry project changes. The
	/// terrain button creates a project material and points each terrain at it.
	/// </para>
	/// </remarks>
	public static class WeatherSurfaceMigration
	{
		public const string LitShaderName = "FishMMO/Weather Lit";
		public const string TerrainShaderName = "FishMMO/Weather Terrain";
		public const string TerrainMaterialPath = "Assets/Prefabs/Client/Materials/Ground/Weather Terrain.mat";
		private const string UrpLit = "Universal Render Pipeline/Lit";
		private const string UrpTerrain = "Universal Render Pipeline/Terrain/Lit";

		/// <summary>
		/// Paths that are not world geometry. Characters, effects and third-party content keep
		/// their shaders; so does anything a designer has already moved off URP's Lit.
		/// </summary>
		private static readonly string[] Skip =
		{
			"/Plugins/",
			"/Models/",
			"/TextMesh Pro/",
			"/Characters/",
			"/FX/",
			"/Effects/",
		};

		[DashboardTool(DashboardToolAttribute.Weather, "Weather-proof world materials", Section = "Surfaces", Order = 0,
			Tooltip = "Moves every world material from URP's Lit onto FishMMO/Weather Lit, so the weather reaches it. Values are kept; characters, effects and plugin materials are skipped.")]
		public static void MigrateMaterialsFromDashboard()
		{
			Shader shader = Shader.Find(LitShaderName);
			if (shader == null)
			{
				Debug.LogError($"[Weather surfaces] The shader '{LitShaderName}' was not found. Is FishWeatherLit.shader in the project and compiling?");
				return;
			}

			var moved = new List<string>();
			var skipped = new List<string>();
			foreach (string guid in AssetDatabase.FindAssets("t:Material"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var material = AssetDatabase.LoadAssetAtPath<Material>(path);
				if (material == null || material.shader == null)
				{
					continue;
				}
				if (material.shader.name != UrpLit)
				{
					continue;
				}
				if (IsSkipped(path))
				{
					skipped.Add(path);
					continue;
				}
				// Swapping the shader drops the material's own tags, and URP's inspector is what
				// normally writes them, so they are carried over by hand.
				string renderType = material.GetTag("RenderType", false, string.Empty);
				material.shader = shader;
				if (!string.IsNullOrEmpty(renderType))
				{
					material.SetOverrideTag("RenderType", renderType);
				}
				EditorUtility.SetDirty(material);
				moved.Add(path);
			}
			AssetDatabase.SaveAssets();

			string list = moved.Count > 0 ? "\n  " + string.Join("\n  ", moved) : string.Empty;
			string left = skipped.Count > 0 ? $"\nLeft alone (characters, effects, plugins): {skipped.Count}." : string.Empty;
			Debug.Log($"[Weather surfaces] {moved.Count} material(s) now take the weather.{list}{left}");
		}

		[DashboardTool(DashboardToolAttribute.Weather, "Weather-proof terrain", Section = "Surfaces", Order = 1,
			Tooltip = "Creates a project terrain material on FishMMO/Weather Terrain and points every terrain in the world scenes at it. Scenes are saved.")]
		public static void MigrateTerrainFromDashboard()
		{
			Material material = EnsureTerrainMaterial();
			if (material == null)
			{
				return;
			}

			SceneSetup[] setup = EditorSceneManager.GetSceneManagerSetup();
			var changed = new List<string>();
			var untouched = new List<string>();
			try
			{
				foreach (string path in WorldScenePaths())
				{
					Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
					int count = 0;
					foreach (Terrain terrain in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
					{
						if (terrain.materialTemplate == material)
						{
							continue;
						}
						Undo.RecordObject(terrain, "Weather terrain");
						terrain.materialTemplate = material;
						EditorUtility.SetDirty(terrain);
						count++;
					}
					if (count > 0)
					{
						EditorSceneManager.MarkSceneDirty(scene);
						EditorSceneManager.SaveScene(scene);
						changed.Add($"{Path.GetFileNameWithoutExtension(path)} ({count})");
					}
					else
					{
						untouched.Add(Path.GetFileNameWithoutExtension(path));
					}
				}
			}
			finally
			{
				if (setup != null && setup.Length > 0)
				{
					EditorSceneManager.RestoreSceneManagerSetup(setup);
				}
			}

			string moved = changed.Count > 0 ? $" Changed: {string.Join(", ", changed)}." : " No terrain needed changing.";
			string same = untouched.Count > 0 ? $" Already right or no terrain: {string.Join(", ", untouched)}." : string.Empty;
			Debug.Log($"[Weather surfaces] {AssetDatabase.GetAssetPath(material)} is the terrain material.{moved}{same}");
		}

		[DashboardTool(DashboardToolAttribute.Weather, "Toggle weather on selected materials", Section = "Surfaces", Order = 2,
			Tooltip = "Turns 'Weather on this surface' off (or back on) for every selected material. URP's own inspector cannot show the property, so this is where a material opts out of rain and snow.")]
		public static void ToggleSelectionFromDashboard()
		{
			Material[] materials = Selection.GetFiltered<Material>(SelectionMode.Assets);
			if (materials.Length == 0)
			{
				Debug.LogWarning("[Weather surfaces] Select one or more materials in the Project window first.");
				return;
			}
			int changed = 0;
			foreach (Material material in materials)
			{
				if (!material.HasProperty(WeatherAmountId))
				{
					continue;
				}
				float amount = material.GetFloat(WeatherAmountId) > 0.5f ? 0f : 1f;
				material.SetFloat(WeatherAmountId, amount);
				EditorUtility.SetDirty(material);
				changed++;
			}
			AssetDatabase.SaveAssets();
			Debug.Log($"[Weather surfaces] {changed} of {materials.Length} selected material(s) toggled. A material on another shader has nothing to toggle.");
		}

		private static readonly int WeatherAmountId = Shader.PropertyToID("_FishWeatherAmount");

		/// <summary>The project's terrain material, created on the weather terrain shader if missing.</summary>
		public static Material EnsureTerrainMaterial()
		{
			var existing = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
			Shader shader = Shader.Find(TerrainShaderName);
			if (shader == null)
			{
				Debug.LogError($"[Weather surfaces] The shader '{TerrainShaderName}' was not found. Is FishWeatherTerrain.shader in the project and compiling?");
				return existing;
			}
			if (existing != null)
			{
				if (existing.shader != shader)
				{
					existing.shader = shader;
					EditorUtility.SetDirty(existing);
					AssetDatabase.SaveAssets();
				}
				return existing;
			}
			WorldEditorAssets.EnsureFolder(Path.GetDirectoryName(TerrainMaterialPath).Replace('\\', '/'));
			var material = new Material(shader) { name = "Weather Terrain" };
			// A hand's depth of snow lifts the ground under it, on the High tier only.
			material.SetFloat("_FishSnowDepth", 0.12f);
			AssetDatabase.CreateAsset(material, TerrainMaterialPath);
			AssetDatabase.SaveAssets();
			return AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
		}

		/// <summary>Every scene the world details name, plus the test beds, in path order.</summary>
		private static IEnumerable<string> WorldScenePaths()
		{
			return AssetDatabase.FindAssets("t:Scene")
				.Select(AssetDatabase.GUIDToAssetPath)
				.Where(path => path.StartsWith("Assets/Scenes/", StringComparison.Ordinal) || path.StartsWith("Assets/TestHarness/", StringComparison.Ordinal))
				.OrderBy(path => path, StringComparer.Ordinal);
		}

		private static bool IsSkipped(string path)
		{
			foreach (string fragment in Skip)
			{
				if (path.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return true;
				}
			}
			return false;
		}
	}
}
