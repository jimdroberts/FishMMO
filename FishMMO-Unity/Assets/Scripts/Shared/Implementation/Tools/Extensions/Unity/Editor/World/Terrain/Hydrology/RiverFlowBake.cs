#if UNITY_EDITOR
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Solves every river of a scene (<see cref="RiverFlowSolver"/>) and keeps the answer in its biome map: a
	/// <see cref="SceneRiverFlow"/> and one field texture per river, all sub-assets of the map's file, reached through
	/// <see cref="SceneBiomeMap.RiverFlow"/>.
	/// </summary>
	/// <remarks>
	/// The map is made an explicit addressable (<see cref="Group"/>) so a reference into it resolves in a build; it
	/// was a dependency of its scene only. The field is laid out as the river's ribbon is: x the metres along its line,
	/// y across it from its right bank (−half) to its left (+half), so the shader reads it with the ribbon's own
	/// along-and-across coordinates.
	/// </remarks>
	public static class RiverFlowBake
	{
		/// <summary>The addressables group the biome maps are kept in: both the server (weather) and the client (water) load them.</summary>
		public const string Group = "Shared_Dynamic";

		/// <summary>Solves the rivers of <paramref name="hydrology"/> into <paramref name="map"/>'s file, replacing any earlier answer. Returns the rivers solved.</summary>
		public static int Bake(SceneHydrology hydrology, SceneBiomeMap map, List<string> notes)
		{
			if (hydrology == null || map == null)
			{
				return 0;
			}
			string path = AssetDatabase.GetAssetPath(map);
			if (string.IsNullOrEmpty(path))
			{
				return 0;
			}
			var clock = System.Diagnostics.Stopwatch.StartNew();
			// The earlier answer out of the file.
			foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(path))
			{
				if (sub is SceneRiverFlow || (sub is Texture2D && sub.name.EndsWith(" Flow")))
				{
					AssetDatabase.RemoveObjectFromAsset(sub);
					Object.DestroyImmediate(sub, true);
				}
			}
			var flow = ScriptableObject.CreateInstance<SceneRiverFlow>();
			flow.name = $"{map.name} River Flow";
			AssetDatabase.AddObjectToAsset(flow, map);
			int solved = 0;
			foreach (SceneHydrology.River river in hydrology.Rivers)
			{
				if (!river.Perennial || river.Points == null || river.Points.Length < 2)
				{
					continue;
				}
				Texture2D field = Solve(river, hydrology.Boulders, out int length);
				if (field == null)
				{
					continue;
				}
				field.name = $"{map.name} River {river.Id} Flow";
				AssetDatabase.AddObjectToAsset(field, map);
				flow.Rivers.Add(new SceneRiverFlow.River { Id = river.Id, Length = length, AlongMetres = RiverFlowSolver.AlongMetres, Field = field });
				solved++;
			}
			EditorUtility.SetDirty(flow);
			AssetDatabase.SaveAssetIfDirty(map);

			Register(path);
			map.RiverFlow = new AssetReferenceT<SceneRiverFlow>(AssetDatabase.AssetPathToGUID(path));
			map.RiverFlow.SetEditorSubObject(flow);
			EditorUtility.SetDirty(map);
			AssetDatabase.SaveAssetIfDirty(map);
			notes?.Add($"River flow: {solved} river(s) solved round {hydrology.Boulders.Count} boulder(s) in {clock.Elapsed.TotalSeconds:0.0} s.");
			return solved;
		}

		/// <summary>Every river of <paramref name="hydrology"/> solved into a flow held in memory, written nowhere: for tools and probes.</summary>
		public static SceneRiverFlow SolveInMemory(SceneHydrology hydrology)
		{
			var flow = ScriptableObject.CreateInstance<SceneRiverFlow>();
			foreach (SceneHydrology.River river in hydrology.Rivers)
			{
				if (!river.Perennial || river.Points == null || river.Points.Length < 2)
				{
					continue;
				}
				Texture2D field = Solve(river, hydrology.Boulders, out int length);
				if (field != null)
				{
					flow.Rivers.Add(new SceneRiverFlow.River { Id = river.Id, Length = length, AlongMetres = RiverFlowSolver.AlongMetres, Field = field });
				}
			}
			return flow;
		}

		/// <summary>One river's field: its line unrolled, its boulders as rock in it, solved.</summary>
		private static Texture2D Solve(SceneHydrology.River river, List<Vector4> boulders, out int length)
		{
			int n = river.Points.Length;
			var along = new float[n];
			for (int i = 1; i < n; i++)
			{
				Vector3 step = river.Points[i] - river.Points[i - 1];
				step.y = 0f;
				along[i] = along[i - 1] + step.magnitude;
			}
			length = Mathf.Max(2, Mathf.CeilToInt(along[n - 1] / RiverFlowSolver.AlongMetres) + 1);
			int ny = RiverFlowSolver.Across;
			var solid = new bool[length * ny];
			foreach (Vector4 boulder in boulders)
			{
				// The nearest point of the line, and how far along and across the boulder stands from it.
				int nearest = -1;
				float best = float.MaxValue;
				for (int i = 0; i < n; i++)
				{
					float dx = boulder.x - river.Points[i].x, dz = boulder.z - river.Points[i].z;
					float d = dx * dx + dz * dz;
					if (d < best)
					{
						best = d;
						nearest = i;
					}
				}
				float width = river.Width != null && nearest < river.Width.Length ? river.Width[nearest] : 4f;
				float radius = Mathf.Max(0.3f, boulder.w);
				if (nearest < 0 || Mathf.Sqrt(best) > 0.5f * width + radius)
				{
					continue;
				}
				Vector3 forward = river.Points[Mathf.Min(n - 1, nearest + 1)] - river.Points[Mathf.Max(0, nearest - 1)];
				forward.y = 0f;
				forward = forward.sqrMagnitude > 1e-8f ? forward.normalized : Vector3.forward;
				var left = new Vector3(-forward.z, 0f, forward.x);
				var offset = new Vector3(boulder.x - river.Points[nearest].x, 0f, boulder.z - river.Points[nearest].z);
				float s = along[nearest] + Vector3.Dot(offset, forward);
				float across = Vector3.Dot(offset, left);
				float cellAcross = width / ny;
				int x0 = Mathf.FloorToInt((s - radius) / RiverFlowSolver.AlongMetres), x1 = Mathf.CeilToInt((s + radius) / RiverFlowSolver.AlongMetres);
				for (int x = Mathf.Max(0, x0); x <= Mathf.Min(length - 1, x1); x++)
				{
					for (int y = 0; y < ny; y++)
					{
						float cellAlong = x * RiverFlowSolver.AlongMetres - s;
						float cellSide = (y + 0.5f) * cellAcross - 0.5f * width - across;
						if (cellAlong * cellAlong + cellSide * cellSide <= radius * radius)
						{
							solid[y * length + x] = true;
						}
					}
				}
			}
			float[] velocity = RiverFlowSolver.Solve(length, solid);
			var field = new Texture2D(length, ny, TextureFormat.RGHalf, false, true)
			{
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
			};
			var pixels = new Color[length * ny];
			for (int y = 0; y < ny; y++)
			{
				for (int x = 0; x < length; x++)
				{
					int c = (y * length + x) * 2;
					pixels[y * length + x] = new Color(velocity[c], velocity[c + 1], 0f, 1f);
				}
			}
			field.SetPixels(pixels);
			// Kept readable: what floats in the river reads the same field on the CPU.
			field.Apply(false, false);
			return field;
		}

		/// <summary>Puts the map's asset in <see cref="Group"/>, addressed by its path; leaves it alone when it is already an entry there.</summary>
		private static void Register(string assetPath)
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			AddressableAssetGroup group = settings != null ? settings.FindGroup(Group) : null;
			if (group == null)
			{
				Debug.LogWarning($"[River flow] No '{Group}' addressables group; '{assetPath}' was not registered and the flow will not load in a build.");
				return;
			}
			string guid = AssetDatabase.AssetPathToGUID(assetPath);
			AddressableAssetEntry entry = settings.FindAssetEntry(guid);
			if (entry == null || entry.parentGroup != group)
			{
				entry = settings.CreateOrMoveEntry(guid, group, false, false);
			}
			if (entry != null && entry.address != assetPath)
			{
				entry.SetAddress(assetPath, false);
			}
			if (entry != null)
			{
				settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true);
			}
		}
	}
}
#endif
