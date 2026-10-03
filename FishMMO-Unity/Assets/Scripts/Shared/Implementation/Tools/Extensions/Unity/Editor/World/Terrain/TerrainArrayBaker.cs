#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>Which of the three arrays a slice belongs to; decides colour space and decoding.</summary>
	public enum TerrainArrayKind
	{
		Albedo,
		Normal,
		Mask,
	}

	/// <summary>Everything a bake of one scene would read, resolved, and the hash of it.</summary>
	public sealed class TerrainArrayPlan
	{
		public string SceneName;
		public readonly List<ResolvedTerrainArrayLayer> Layers = new List<ResolvedTerrainArrayLayer>();
		public readonly List<TerrainArrayLayerParams> Params = new List<TerrainArrayLayerParams>();
		/// <summary>LOCAL overrides that disagreed, and overrides that had to fall back. For the log.</summary>
		public readonly List<string> Conflicts = new List<string>();
		public readonly List<string> Problems = new List<string>();
		public int SliceSize;
		public bool AnyNormal;
		public bool AnyMask;
		public string Hash;
	}

	/// <summary>What one bake did.</summary>
	public sealed class TerrainArrayBakeReport
	{
		public string SceneName;
		/// <summary>True when the arrays were written; false when they were already current or could not be.</summary>
		public bool Baked;
		public bool UpToDate;
		public readonly List<string> Written = new List<string>();
		public readonly List<string> Conflicts = new List<string>();
		public readonly List<string> Problems = new List<string>();

		public override string ToString()
		{
			string state = Baked ? "baked" : UpToDate ? "up to date" : "not baked";
			return $"'{SceneName}': {state}; {Written.Count} file(s), {Conflicts.Count} LOCAL conflict(s), {Problems.Count} problem(s)";
		}
	}

	/// <summary>
	/// Builds a scene's terrain texture arrays from the art its layers resolve to, LOCAL-first.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Build output, rebuilt per machine, never committed.</b> The arrays may hold licensed art
	/// from <c>Assets/LOCAL</c>, so they live in <see cref="TerrainArraySet.BakedDirectory"/>, which
	/// is gitignored, and nothing committed references them (<see cref="TerrainArraySet"/> explains
	/// how the binder finds them). The editor needs no manual step: <see cref="TerrainArrayAutoBake"/>
	/// rebakes a scene whose set is missing or stale when it is opened or saved, and a client build
	/// bakes every world scene before it builds.
	/// </para>
	/// <para>
	/// <b>Cached by a content hash.</b> The hash covers what the bake reads — each resolved texture's
	/// asset and import dependency hash, each layer's numbers, the slice size, the bake's own
	/// version — so a scene is rebaked when its art changes and not otherwise. A LOCAL file
	/// appearing, disappearing or changing changes what resolves, and so the hash.
	/// </para>
	/// <para>
	/// <b>Why flipbook images and Unity's importer</b>, rather than Texture2DArray assets written
	/// from script. Each array is written as one image of its slices in a grid, and imported with the
	/// Texture2DArray shape. The importer then does what a script would have to reimplement and keep
	/// correct per platform: mipmaps per slice, the compressed format for whichever build target is
	/// active (BC7 on desktop, ASTC or ETC where those are what the platform has), re-compression on
	/// a target switch, and a non-readable texture that keeps no CPU copy in a player. Images are
	/// at most 16384 on a side, so slices are capped at <see cref="MaximumSliceSize"/>.
	/// </para>
	/// <para>
	/// <b>Precedent: the world map bake</b> (gitignored folder, found by path convention, a client
	/// build registers it in a "Client" addressable group and removes the group afterwards, no scene
	/// writes during a build). One deliberate difference: the baked folder is NOT deleted after a
	/// build. Maps are only needed by a player; the arrays are what the editor draws the ground with,
	/// and deleting them would cost a full rebake the next time anyone opened a scene.
	/// </para>
	/// </remarks>
	public static class TerrainArrayBaker
	{
		/// <summary>Raised whenever the bake's output format changes, so every machine rebakes.</summary>
		public const int BakeVersion = 1;

		/// <summary>The largest slice. 32 slices of 1024 are a 16384 x 2048 image; 2048 would need 16384 x 8192 and half a gigabyte to write.</summary>
		public const int MaximumSliceSize = 1024;

		/// <summary>The smallest slice: placeholder layers are 8 pixels, and mips below this are not worth a slice.</summary>
		public const int MinimumSliceSize = 128;

		/// <summary>The widest image Unity imports.</summary>
		public const int MaximumImageEdge = 16384;

		private static bool busy;

		/// <summary>True while a bake runs; the auto-baker waits.</summary>
		public static bool IsBusy => busy;

		// ── Planning ──────────────────────────────────────────────────

		/// <summary>Resolves every layer of a binder's scene and hashes what a bake would read.</summary>
		public static TerrainArrayPlan Plan(TerrainArrayBinder binder, ITerrainArrayLocalLookup lookup = null)
		{
			var plan = new TerrainArrayPlan { SceneName = binder != null ? binder.SceneKey : null };
			TerrainArrayProvenance provenance = binder != null ? binder.Provenance : null;
			if (provenance == null)
			{
				plan.Problems.Add("The binder has no provenance child; nothing to bake.");
				return plan;
			}
			return Plan(plan.SceneName, provenance.Layers, lookup ?? new ProjectTerrainArrayLocalLookup());
		}

		/// <summary>Resolves a list of layers and hashes what a bake would read. Pure apart from the lookup and asset hashes.</summary>
		public static TerrainArrayPlan Plan(string sceneName, IReadOnlyList<TerrainArrayLayerSource> layers, ITerrainArrayLocalLookup lookup)
		{
			var plan = new TerrainArrayPlan { SceneName = sceneName };
			int count = layers != null ? Mathf.Min(layers.Count, TerrainArrayLayerParams.MaximumLayers) : 0;
			if (layers != null && layers.Count > TerrainArrayLayerParams.MaximumLayers)
			{
				plan.Problems.Add($"{layers.Count} layers; the arrays hold {TerrainArrayLayerParams.MaximumLayers} and the rest draw as layer {TerrainArrayLayerParams.MaximumLayers - 1}.");
			}

			int largest = 0;
			for (int i = 0; i < count; i++)
			{
				ResolvedTerrainArrayLayer resolved = TerrainArrayResolver.Resolve(i, layers[i], lookup, plan.Conflicts);
				plan.Layers.Add(resolved);
				bool hasAlpha = AlbedoHasAlpha(resolved.Albedo);
				plan.Params.Add(TerrainArrayLayerParams.From(resolved.ParamsLayer, resolved.Mask != null, hasAlpha));
				plan.AnyNormal |= resolved.Normal != null;
				plan.AnyMask |= resolved.Mask != null;
				largest = Mathf.Max(largest, Largest(resolved.Albedo), Largest(resolved.Normal), Largest(resolved.Mask));
				if (resolved.Albedo == null)
				{
					plan.Problems.Add($"Layer {i} '{(resolved.Committed != null ? resolved.Committed.name : "missing")}' has no albedo; its slice is grey.");
				}
			}
			plan.SliceSize = SliceSizeFor(largest);
			plan.Hash = HashOf(plan);
			return plan;
		}

		/// <summary>
		/// The slice edge for the largest source edge: the next power of two, within
		/// [<see cref="MinimumSliceSize"/>, <see cref="MaximumSliceSize"/>].
		/// </summary>
		/// <remarks>A power of two so every mip is whole and block compression never pads.</remarks>
		public static int SliceSizeFor(int largestSourceEdge)
		{
			int size = Mathf.NextPowerOfTwo(Mathf.Max(1, largestSourceEdge));
			return Mathf.Clamp(size, MinimumSliceSize, MaximumSliceSize);
		}

		/// <summary>
		/// The grid a flipbook of <paramref name="count"/> slices is written as: as few rows as fit in
		/// <see cref="MaximumImageEdge"/>, then as few columns as hold every slice in those rows, so
		/// the padding is under one row.
		/// </summary>
		public static void Layout(int count, int sliceSize, out int columns, out int rows)
		{
			int perRow = Mathf.Max(1, MaximumImageEdge / Mathf.Max(1, sliceSize));
			count = Mathf.Max(1, count);
			rows = (count + perRow - 1) / perRow;
			columns = (count + rows - 1) / rows;
		}

		/// <summary>
		/// The bottom-left pixel of slice <paramref name="index"/> in a flipbook image (pixel rows run
		/// bottom-up, as Texture2D stores them). Unity reads a flipbook's cells left to right from the
		/// TOP row down, so slice 0 is the top-left cell.
		/// </summary>
		public static Vector2Int SliceOrigin(int index, int columns, int rows, int sliceSize)
		{
			int column = index % columns;
			int rowFromTop = index / columns;
			return new Vector2Int(column * sliceSize, (rows - 1 - rowFromTop) * sliceSize);
		}

		/// <summary>The flipbook's file name; the importer reads the grid back out of it.</summary>
		public static string FlipbookFileName(TerrainArrayKind kind, int columns, int rows) => $"{kind} {columns}x{rows}.png";

		/// <summary>Parses a flipbook path written by this baker. False for anything else.</summary>
		public static bool TryParseFlipbook(string assetPath, out TerrainArrayKind kind, out int columns, out int rows)
		{
			kind = TerrainArrayKind.Albedo;
			columns = rows = 0;
			if (string.IsNullOrEmpty(assetPath))
			{
				return false;
			}
			string path = assetPath.Replace('\\', '/');
			if (!path.StartsWith(TerrainArraySet.BakedDirectory + "/", StringComparison.Ordinal) || !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
			string name = Path.GetFileNameWithoutExtension(path);
			int space = name.IndexOf(' ');
			if (space <= 0 || !Enum.TryParse(name.Substring(0, space), out kind))
			{
				return false;
			}
			string[] grid = name.Substring(space + 1).Split('x');
			return grid.Length == 2
				&& int.TryParse(grid[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out columns)
				&& int.TryParse(grid[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out rows)
				&& columns > 0 && rows > 0;
		}

		/// <summary>True when a binder's baked set is missing or was baked from different inputs.</summary>
		public static bool IsStale(TerrainArrayBinder binder, out TerrainArrayPlan plan)
		{
			plan = Plan(binder);
			if (string.IsNullOrEmpty(plan.SceneName) || plan.Layers.Count == 0)
			{
				return false;
			}
			var set = AssetDatabase.LoadAssetAtPath<TerrainArraySet>(TerrainArraySet.BakedAssetPath(plan.SceneName));
			return set == null || !set.IsUsable || set.ContentHash != plan.Hash;
		}

		private static int Largest(Texture2D texture) => texture != null ? Mathf.Max(texture.width, texture.height) : 0;

		/// <summary>Whether a texture's alpha carries data (smoothness, for an albedo), as its importer sees the source.</summary>
		public static bool AlbedoHasAlpha(Texture2D texture)
		{
			if (texture == null)
			{
				return false;
			}
			if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) is TextureImporter importer)
			{
				return importer.alphaSource != TextureImporterAlphaSource.None && importer.DoesSourceTextureHaveAlpha();
			}
			return GraphicsFormatUtility.HasAlphaChannel(texture.graphicsFormat);
		}

		private static string HashOf(TerrainArrayPlan plan)
		{
			var text = new StringBuilder(1024);
			text.Append("v").Append(BakeVersion)
				.Append("|cs").Append((int)PlayerSettings.colorSpace)
				.Append("|s").Append(plan.SliceSize)
				.Append("|n").Append(plan.AnyNormal ? 1 : 0)
				.Append("|m").Append(plan.AnyMask ? 1 : 0);
			TerrainArrayLayerParams.Allocate(out Vector4[] st, out Vector4[] tint, out Vector4[] maskOffset, out Vector4[] maskScale, out Vector4[] surface);
			for (int i = 0; i < plan.Layers.Count; i++)
			{
				plan.Params[i].Pack(i, st, tint, maskOffset, maskScale, surface);
				ResolvedTerrainArrayLayer layer = plan.Layers[i];
				text.Append("|L").Append(i)
					.Append(":a=").Append(TextureKey(layer.Albedo))
					.Append(":n=").Append(TextureKey(layer.Normal))
					.Append(":m=").Append(TextureKey(layer.Mask))
					.Append(":p=").Append(Vec(st[i])).Append(Vec(tint[i])).Append(Vec(maskOffset[i])).Append(Vec(maskScale[i])).Append(Vec(surface[i]));
			}
			return Hash128.Compute(text.ToString()).ToString();
		}

		private static string Vec(Vector4 v) => string.Format(CultureInfo.InvariantCulture, "({0:R},{1:R},{2:R},{3:R})", v.x, v.y, v.z, v.w);

		/// <summary>A texture's identity and content for the hash: its path and the import's dependency hash.</summary>
		private static string TextureKey(Texture2D texture)
		{
			if (texture == null)
			{
				return "-";
			}
			string path = AssetDatabase.GetAssetPath(texture);
			if (string.IsNullOrEmpty(path))
			{
				return "mem:" + texture.name + ":" + texture.width + "x" + texture.height;
			}
			return path + "#" + AssetDatabase.GetAssetDependencyHash(path);
		}

		// ── Baking ────────────────────────────────────────────────────

		/// <summary>
		/// Bakes one binder's scene if its set is missing or stale (or always, when forced), then
		/// re-binds every open scene so the ground shows it at once.
		/// </summary>
		public static TerrainArrayBakeReport Bake(TerrainArrayBinder binder, bool force = false)
		{
			var report = new TerrainArrayBakeReport { SceneName = binder != null ? binder.SceneKey : null };
			if (binder == null)
			{
				report.Problems.Add("No binder.");
				return report;
			}
			if (string.IsNullOrEmpty(report.SceneName))
			{
				report.Problems.Add("The scene has not been saved yet, so it has no name to bake under; it is baked when it is saved.");
				return report;
			}
			if (busy)
			{
				report.Problems.Add("Another bake is running.");
				return report;
			}

			TerrainArrayPlan plan = Plan(binder);
			report.Conflicts.AddRange(plan.Conflicts);
			report.Problems.AddRange(plan.Problems);
			if (plan.Layers.Count == 0)
			{
				return report;
			}

			var existing = AssetDatabase.LoadAssetAtPath<TerrainArraySet>(TerrainArraySet.BakedAssetPath(plan.SceneName));
			if (!force && existing != null && existing.IsUsable && existing.ContentHash == plan.Hash)
			{
				report.UpToDate = true;
				return report;
			}

			busy = true;
			try
			{
				Write(plan, report);
			}
			finally
			{
				busy = false;
				EditorUtility.ClearProgressBar();
			}

			foreach (string conflict in report.Conflicts)
			{
				Debug.LogWarning($"[Terrain arrays] {plan.SceneName}: {conflict}");
			}
			foreach (string problem in report.Problems)
			{
				Debug.LogWarning($"[Terrain arrays] {plan.SceneName}: {problem}");
			}
			if (report.Baked)
			{
				var summary = new StringBuilder();
				foreach (ResolvedTerrainArrayLayer layer in plan.Layers)
				{
					summary.Append("\n  ").Append(layer.LayerIndex).Append(' ').Append(layer.Describe());
				}
				Debug.Log($"[Terrain arrays] Baked '{plan.SceneName}': {plan.Layers.Count} layer(s) at {plan.SliceSize}px{(plan.AnyNormal ? ", normals" : "")}{(plan.AnyMask ? ", masks" : "")}. Build output in '{TerrainArraySet.BakedFolder(plan.SceneName)}' (gitignored).{summary}");
			}
			TerrainArrayBinder.BindAllLoaded();
			return report;
		}

		/// <summary>Bakes every binder in a loaded scene. Normally there is one.</summary>
		public static List<TerrainArrayBakeReport> BakeScene(Scene scene, bool force = false)
		{
			var reports = new List<TerrainArrayBakeReport>();
			foreach (TerrainArrayBinder binder in BindersIn(scene))
			{
				reports.Add(Bake(binder, force));
			}
			return reports;
		}

		/// <summary>The binders in a loaded scene, inactive ones included.</summary>
		public static List<TerrainArrayBinder> BindersIn(Scene scene)
		{
			var binders = new List<TerrainArrayBinder>();
			if (!scene.IsValid() || !scene.isLoaded)
			{
				return binders;
			}
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				binders.AddRange(root.GetComponentsInChildren<TerrainArrayBinder>(true));
			}
			return binders;
		}

		private static void Write(TerrainArrayPlan plan, TerrainArrayBakeReport report)
		{
			string folder = TerrainArraySet.BakedFolder(plan.SceneName);
			WorldEditorAssets.EnsureFolder(folder);

			int count = plan.Layers.Count;
			int size = plan.SliceSize;
			Layout(count, size, out int columns, out int rows);

			var keep = new HashSet<string>(StringComparer.Ordinal);
			string albedoPath = WriteFlipbook(plan, TerrainArrayKind.Albedo, folder, columns, rows, report);
			keep.Add(albedoPath);
			string normalPath = null, maskPath = null;
			if (plan.AnyNormal)
			{
				normalPath = WriteFlipbook(plan, TerrainArrayKind.Normal, folder, columns, rows, report);
				keep.Add(normalPath);
			}
			if (plan.AnyMask)
			{
				maskPath = WriteFlipbook(plan, TerrainArrayKind.Mask, folder, columns, rows, report);
				keep.Add(maskPath);
			}

			string setPath = TerrainArraySet.BakedAssetPath(plan.SceneName);
			keep.Add(setPath);
			// Images from an earlier bake with another grid (the file name carries it) would otherwise linger.
			foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { folder }))
			{
				string stale = AssetDatabase.GUIDToAssetPath(guid);
				if (!keep.Contains(stale) && !AssetDatabase.IsValidFolder(stale))
				{
					AssetDatabase.DeleteAsset(stale);
				}
			}

			EditorUtility.DisplayProgressBar("Terrain arrays", $"Importing '{plan.SceneName}' (compressing {columns * rows} slices per array)", 0.9f);
			foreach (string path in new[] { albedoPath, normalPath, maskPath })
			{
				if (path != null)
				{
					// TerrainArrayTextureImporter sets the array shape and grid before the first import.
					AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
				}
			}

			var set = AssetDatabase.LoadAssetAtPath<TerrainArraySet>(setPath);
			bool created = set == null;
			if (created)
			{
				set = ScriptableObject.CreateInstance<TerrainArraySet>();
			}
			set.SceneName = plan.SceneName;
			set.Albedo = AssetDatabase.LoadAssetAtPath<Texture2DArray>(albedoPath);
			set.Normal = normalPath != null ? AssetDatabase.LoadAssetAtPath<Texture2DArray>(normalPath) : null;
			set.Mask = maskPath != null ? AssetDatabase.LoadAssetAtPath<Texture2DArray>(maskPath) : null;
			set.LayerCount = count;
			set.LayerNames = new string[count];
			set.LayerSources = new string[count];
			TerrainArrayLayerParams.Allocate(out set.LayerST, out set.LayerTint, out set.LayerMaskOffset, out set.LayerMaskScale, out set.LayerSurface);
			for (int i = 0; i < count; i++)
			{
				plan.Params[i].Pack(i, set.LayerST, set.LayerTint, set.LayerMaskOffset, set.LayerMaskScale, set.LayerSurface);
				set.LayerNames[i] = plan.Layers[i].Committed != null ? plan.Layers[i].Committed.name : string.Empty;
				set.LayerSources[i] = plan.Layers[i].Describe();
			}
			set.ContentHash = plan.Hash;
			if (set.Albedo == null)
			{
				report.Problems.Add($"'{albedoPath}' did not import as a texture array, so the ground stays grey; check the import log.");
				// Not a usable hash: try again next time rather than caching a broken set.
				set.ContentHash = string.Empty;
			}

			if (created)
			{
				AssetDatabase.CreateAsset(set, setPath);
			}
			else
			{
				EditorUtility.SetDirty(set);
			}
			AssetDatabase.SaveAssets();
			report.Written.Add(setPath);
			report.Baked = set.Albedo != null;
		}

		/// <summary>Renders every slice of one array and writes the flipbook image. Returns its path.</summary>
		private static string WriteFlipbook(TerrainArrayPlan plan, TerrainArrayKind kind, string folder, int columns, int rows, TerrainArrayBakeReport report)
		{
			int size = plan.SliceSize;
			int width = columns * size, height = rows * size;
			var pixels = new Color32[width * height];
			Color32 neutral = Neutral(kind);
			for (int i = 0; i < pixels.Length; i++)
			{
				pixels[i] = neutral;
			}

			for (int i = 0; i < plan.Layers.Count; i++)
			{
				EditorUtility.DisplayProgressBar("Terrain arrays", $"'{plan.SceneName}': {kind} {i + 1}/{plan.Layers.Count}", (i + (int)kind * plan.Layers.Count) / (3f * plan.Layers.Count));
				ResolvedTerrainArrayLayer layer = plan.Layers[i];
				Texture2D source = kind == TerrainArrayKind.Albedo ? layer.Albedo : kind == TerrainArrayKind.Normal ? layer.Normal : layer.Mask;
				if (source == null)
				{
					continue;
				}
				Color32[] slice = TerrainArraySliceReader.Read(source, size, kind, report.Problems);
				if (slice == null)
				{
					continue;
				}
				if (kind == TerrainArrayKind.Albedo && !plan.Params[i].AlbedoHasAlpha)
				{
					// No smoothness in the alpha: the shader multiplies 1 by the slider, as TerrainLit does.
					for (int p = 0; p < slice.Length; p++)
					{
						slice[p].a = 255;
					}
				}
				Vector2Int origin = SliceOrigin(i, columns, rows, size);
				for (int y = 0; y < size; y++)
				{
					Array.Copy(slice, y * size, pixels, (origin.y + y) * width + origin.x, size);
				}
			}

			var image = new Texture2D(width, height, TextureFormat.RGBA32, false, kind != TerrainArrayKind.Albedo);
			image.SetPixels32(pixels);
			image.Apply(false, false);
			string path = $"{folder}/{FlipbookFileName(kind, columns, rows)}";
			File.WriteAllBytes(path, image.EncodeToPNG());
			Object.DestroyImmediate(image);
			report.Written.Add(path);
			return path;
		}

		/// <summary>What an empty slice holds: grey albedo, a flat normal, the "no mask" grey TerrainLit assumes.</summary>
		public static Color32 Neutral(TerrainArrayKind kind)
		{
			switch (kind)
			{
				case TerrainArrayKind.Normal: return new Color32(128, 128, 255, 255);
				case TerrainArrayKind.Mask: return new Color32(128, 128, 128, 128);
				default: return new Color32(128, 128, 128, 255);
			}
		}

		// ── Builds ────────────────────────────────────────────────────

		/// <summary>
		/// Brings every world scene's arrays up to date and registers them for a client build.
		/// </summary>
		/// <remarks>
		/// Called by the build tool before the world maps are baked, because the maps photograph the
		/// ground and must see the arrays. Scenes are opened to read their provenance and closed
		/// again; nothing is written to them. Only scenes that use the binder script are opened at all.
		/// </remarks>
		public static int BakeForBuild(List<string> log)
		{
			int baked = BakeWorldScenes(log);
			RegisterForBuild(log);
			return baked;
		}

		/// <summary>Bakes every stale world scene. Opens each scene that has a binder (unless already open), reads it, closes it.</summary>
		public static int BakeWorldScenes(List<string> log)
		{
			string binderScript = BinderScriptPath();
			int baked = 0;
			List<string> scenes = WorldEditorAssets.WorldScenePaths();
			TerrainArrayAutoBake.Suspend();
			try
			{
				for (int i = 0; i < scenes.Count; i++)
				{
					string path = scenes[i].Replace('\\', '/');
					if (binderScript != null && Array.IndexOf(AssetDatabase.GetDependencies(path, false), binderScript) < 0)
					{
						continue;
					}
					Scene scene = SceneManager.GetSceneByPath(path);
					bool opened = false;
					if (!scene.IsValid() || !scene.isLoaded)
					{
						scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
						opened = true;
					}
					try
					{
						foreach (TerrainArrayBakeReport report in BakeScene(scene))
						{
							log?.Add(report.ToString());
							log?.AddRange(report.Conflicts);
							log?.AddRange(report.Problems);
							if (report.Baked)
							{
								baked++;
							}
						}
					}
					finally
					{
						if (opened)
						{
							// Read only: closed without saving.
							EditorSceneManager.CloseScene(scene, true);
						}
					}
				}
			}
			finally
			{
				TerrainArrayAutoBake.Resume();
			}
			return baked;
		}

		/// <summary>Puts the baked set of every world scene being built in the client-only addressable group, addressed by its scene.</summary>
		/// <remarks>
		/// Only the world scenes a build includes, never every set in the cache: the cache also holds
		/// sets for private scene copies under Assets/LOCAL, whose arrays carry LOCAL art and whose
		/// scenes never ship, so registering them would put licensed textures into a build for no scene.
		/// </remarks>
		public static int RegisterForBuild(List<string> log)
		{
			var built = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
			foreach (string scenePath in WorldEditorAssets.WorldScenePaths())
			{
				built.Add(System.IO.Path.GetFileNameWithoutExtension(scenePath));
			}
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			if (settings == null)
			{
				log?.Add("Addressables is not initialised, so no terrain arrays reach the build.");
				return 0;
			}
			if (!AssetDatabase.IsValidFolder(TerrainArraySet.BakedDirectory))
			{
				return 0;
			}
			int registered = 0;
			AddressableAssetGroup group = null;
			foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(TerrainArraySet), new[] { TerrainArraySet.BakedDirectory }))
			{
				var set = AssetDatabase.LoadAssetAtPath<TerrainArraySet>(AssetDatabase.GUIDToAssetPath(guid));
				if (set == null || !set.IsUsable || string.IsNullOrEmpty(set.SceneName))
				{
					continue;
				}
				if (!built.Contains(set.SceneName))
				{
					log?.Add($"Skipped the terrain arrays of '{set.SceneName}': not a world scene this build includes.");
					continue;
				}
				if (group == null)
				{
					group = settings.FindGroup(TerrainArraySet.AddressableGroupName)
						?? settings.CreateGroup(TerrainArraySet.AddressableGroupName, false, false, false, null,
							settings.DefaultGroup.Schemas.ConvertAll(schema => schema.GetType()).ToArray());
				}
				AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group);
				if (entry != null)
				{
					entry.address = TerrainArraySet.AddressOf(set.SceneName);
					registered++;
				}
			}
			log?.Add($"Registered {registered} terrain array set(s) in '{TerrainArraySet.AddressableGroupName}'.");
			return registered;
		}

		/// <summary>Removes the build's addressable group. The baked folder stays: it is the editor's cache.</summary>
		public static void RemoveBuildGroup()
		{
			AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
			AddressableAssetGroup group = settings != null ? settings.FindGroup(TerrainArraySet.AddressableGroupName) : null;
			if (group != null)
			{
				settings.RemoveGroup(group);
				AssetDatabase.SaveAssets();
			}
		}

		/// <summary>Deletes every baked set and the group. Open scenes rebake on their next check.</summary>
		public static void CleanBaked()
		{
			RemoveBuildGroup();
			if (AssetDatabase.IsValidFolder(TerrainArraySet.BakedDirectory))
			{
				AssetDatabase.DeleteAsset(TerrainArraySet.BakedDirectory);
			}
			AssetDatabase.Refresh();
			TerrainArrayBinder.BindAllLoaded();
			Debug.Log($"[Terrain arrays] Removed '{TerrainArraySet.BakedDirectory}' and the '{TerrainArraySet.AddressableGroupName}' group.");
		}

		/// <summary>The binder's script path, for the cheap "does this scene use it" test; null if it cannot be found.</summary>
		private static string BinderScriptPath()
		{
			foreach (string guid in AssetDatabase.FindAssets("TerrainArrayBinder t:MonoScript"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				if (Path.GetFileName(path) == "TerrainArrayBinder.cs")
				{
					return path;
				}
			}
			return null;
		}

		// ── Dashboard ─────────────────────────────────────────────────

		[DashboardTool(DashboardToolAttribute.Maintenance, "Bake terrain arrays (open scenes)", Section = "Content", Order = 7,
			Tooltip = "Rebuilds the texture arrays of every open scene that uses the array terrain shader, LOCAL art first. Build output: gitignored.")]
		public static void BakeOpenScenesFromDashboard()
		{
			for (int i = 0; i < SceneManager.sceneCount; i++)
			{
				foreach (TerrainArrayBakeReport report in BakeScene(SceneManager.GetSceneAt(i), force: true))
				{
					Debug.Log($"[Terrain arrays] {report}");
				}
			}
		}

		[DashboardTool(DashboardToolAttribute.Maintenance, "Remove baked terrain arrays", Section = "Content", Order = 8,
			Tooltip = "Deletes every scene's baked terrain arrays. Open scenes bake again on their next open or save.")]
		public static void CleanFromDashboard()
		{
			CleanBaked();
		}
	}

	/// <summary>
	/// Reads one source texture into a slice of the given size, decoded into what the array stores.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Through the GPU when there is one.</b> A blit reads any texture Unity can import — compressed,
	/// non-readable, PSD, EXR — without touching its importer (sources are committed or LOCAL assets
	/// whose settings are not the bake's to change), resizes it through its own mips, and decodes sRGB
	/// on the way in and encodes it on the way out, so an albedo's bytes come back exactly as stored.
	/// </para>
	/// <para>
	/// <b>Normal maps are decoded here</b>, from whatever layout the platform keeps them in (x in red
	/// or in alpha, as URP's UnpackNormalmapRGorAG reads them), and written back as plain RGB, which is
	/// what the array shader decodes on every platform.
	/// </para>
	/// <para>
	/// <b>Headless (-nographics) there is no GPU</b>, so a readable texture is read directly and an
	/// unreadable PNG or JPG is decoded from its file; anything else cannot be read and its slice stays
	/// neutral, with a problem reported.
	/// </para>
	/// </remarks>
	public static class TerrainArraySliceReader
	{
		public static Color32[] Read(Texture2D source, int size, TerrainArrayKind kind, List<string> problems)
		{
			Color32[] pixels = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null
				? ReadThroughGpu(source, size, kind)
				: ReadOnCpu(source, size, problems);
			if (pixels == null)
			{
				return null;
			}
			if (kind == TerrainArrayKind.Normal)
			{
				for (int i = 0; i < pixels.Length; i++)
				{
					pixels[i] = EncodeNormal(pixels[i]);
				}
			}
			return pixels;
		}

		/// <summary>
		/// A packed normal, whichever layout it is in, as plain RGB. x is red times alpha: one of the two
		/// is 1 in every layout Unity uses (RGB with opaque alpha, DXT5nm with red at 1, BC5 with alpha 1).
		/// </summary>
		public static Color32 EncodeNormal(Color32 packed)
		{
			float x = packed.r / 255f * (packed.a / 255f) * 2f - 1f;
			float y = packed.g / 255f * 2f - 1f;
			float z = Mathf.Sqrt(Mathf.Max(0f, 1f - x * x - y * y));
			return new Color32(
				(byte)Mathf.RoundToInt(Mathf.Clamp01(x * 0.5f + 0.5f) * 255f),
				(byte)Mathf.RoundToInt(Mathf.Clamp01(y * 0.5f + 0.5f) * 255f),
				(byte)Mathf.RoundToInt(Mathf.Clamp01(z * 0.5f + 0.5f) * 255f),
				255);
		}

		private static Color32[] ReadThroughGpu(Texture2D source, int size, TerrainArrayKind kind)
		{
			bool linear = kind != TerrainArrayKind.Albedo;
			var descriptor = new RenderTextureDescriptor(size, size, linear ? GraphicsFormat.R8G8B8A8_UNorm : GraphicsFormat.R8G8B8A8_SRGB, 0)
			{
				useMipMap = false,
				msaaSamples = 1,
			};
			RenderTexture target = RenderTexture.GetTemporary(descriptor);
			RenderTexture previous = RenderTexture.active;
			var readback = new Texture2D(size, size, TextureFormat.RGBA32, false, linear);
			try
			{
				Graphics.Blit(source, target);
				RenderTexture.active = target;
				readback.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
				readback.Apply(false, false);
				return readback.GetPixels32();
			}
			finally
			{
				RenderTexture.active = previous;
				RenderTexture.ReleaseTemporary(target);
				Object.DestroyImmediate(readback);
			}
		}

		private static Color32[] ReadOnCpu(Texture2D source, int size, List<string> problems)
		{
			Color32[] pixels = null;
			int width = 0, height = 0;
			if (source.isReadable)
			{
				pixels = source.GetPixels32(0);
				width = source.width;
				height = source.height;
			}
			else
			{
				string path = AssetDatabase.GetAssetPath(source);
				string extension = Path.GetExtension(path).ToLowerInvariant();
				if (extension == ".png" || extension == ".jpg" || extension == ".jpeg")
				{
					var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
					try
					{
						if (decoded.LoadImage(File.ReadAllBytes(path)))
						{
							pixels = decoded.GetPixels32();
							width = decoded.width;
							height = decoded.height;
						}
					}
					finally
					{
						Object.DestroyImmediate(decoded);
					}
				}
			}
			if (pixels == null)
			{
				problems?.Add($"'{source.name}' cannot be read without a graphics device (not readable, not a PNG or JPG); its slice is neutral. Bake with a GPU.");
				return null;
			}
			return Resize(pixels, width, height, size);
		}

		/// <summary>A box-filtered resize to a square: averages every source pixel under each target pixel, or samples bilinearly when enlarging.</summary>
		public static Color32[] Resize(Color32[] source, int width, int height, int size)
		{
			var result = new Color32[size * size];
			for (int y = 0; y < size; y++)
			{
				float y0 = y * (float)height / size, y1 = (y + 1) * (float)height / size;
				for (int x = 0; x < size; x++)
				{
					float x0 = x * (float)width / size, x1 = (x + 1) * (float)width / size;
					if (x1 - x0 <= 1f || y1 - y0 <= 1f)
					{
						result[y * size + x] = Bilinear(source, width, height, (x0 + x1) * 0.5f - 0.5f, (y0 + y1) * 0.5f - 0.5f);
						continue;
					}
					int sx0 = (int)x0, sx1 = Mathf.Min(width, Mathf.CeilToInt(x1));
					int sy0 = (int)y0, sy1 = Mathf.Min(height, Mathf.CeilToInt(y1));
					float r = 0, g = 0, b = 0, a = 0;
					int n = 0;
					for (int sy = sy0; sy < sy1; sy++)
					{
						for (int sx = sx0; sx < sx1; sx++)
						{
							Color32 c = source[sy * width + sx];
							r += c.r; g += c.g; b += c.b; a += c.a;
							n++;
						}
					}
					n = Mathf.Max(1, n);
					result[y * size + x] = new Color32((byte)(r / n + 0.5f), (byte)(g / n + 0.5f), (byte)(b / n + 0.5f), (byte)(a / n + 0.5f));
				}
			}
			return result;
		}

		private static Color32 Bilinear(Color32[] source, int width, int height, float x, float y)
		{
			x = Mathf.Clamp(x, 0f, width - 1);
			y = Mathf.Clamp(y, 0f, height - 1);
			int ix = (int)x, iy = (int)y;
			int jx = Mathf.Min(ix + 1, width - 1), jy = Mathf.Min(iy + 1, height - 1);
			float fx = x - ix, fy = y - iy;
			Color a = source[iy * width + ix], b = source[iy * width + jx], c = source[jy * width + ix], d = source[jy * width + jx];
			return Color.Lerp(Color.Lerp(a, b, fx), Color.Lerp(c, d, fx), fy);
		}
	}

	/// <summary>
	/// Imports the baker's flipbook images as texture arrays, with the grid their file name records.
	/// </summary>
	/// <remarks>
	/// Set before the first import rather than after it, so a 16384-wide image is compressed once, as
	/// an array, instead of once as a flat texture and again as an array. It runs on every import of
	/// these files — a platform switch, a Library rebuild — so the settings never drift.
	/// </remarks>
	public sealed class TerrainArrayTextureImporter : AssetPostprocessor
	{
		public override uint GetVersion() => 1;

		private void OnPreprocessTexture()
		{
			if (!TerrainArrayBaker.TryParseFlipbook(assetPath, out TerrainArrayKind kind, out int columns, out int rows))
			{
				return;
			}
			var importer = (TextureImporter)assetImporter;
			importer.textureType = TextureImporterType.Default;

			var settings = new TextureImporterSettings();
			importer.ReadTextureSettings(settings);
			settings.textureShape = TextureImporterShape.Texture2DArray;
			settings.flipbookColumns = columns;
			settings.flipbookRows = rows;
			importer.SetTextureSettings(settings);

			importer.sRGBTexture = kind == TerrainArrayKind.Albedo;
			importer.alphaSource = kind == TerrainArrayKind.Normal ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
			importer.alphaIsTransparency = false;
			// A grid of 9 slices is 9216 wide: rounding it to a power of two would cut slices in half.
			importer.npotScale = TextureImporterNPOTScale.None;
			importer.mipmapEnabled = true;
			importer.streamingMipmaps = false;
			importer.isReadable = false;
			importer.wrapMode = TextureWrapMode.Repeat;
			importer.filterMode = FilterMode.Trilinear;
			importer.anisoLevel = 4;
			importer.maxTextureSize = TerrainArrayBaker.MaximumImageEdge;
			importer.textureCompression = TextureImporterCompression.CompressedHQ;

			TextureImporterPlatformSettings platform = importer.GetDefaultPlatformTextureSettings();
			platform.maxTextureSize = TerrainArrayBaker.MaximumImageEdge;
			platform.textureCompression = TextureImporterCompression.CompressedHQ;
			importer.SetPlatformTextureSettings(platform);
		}
	}
}
#endif
