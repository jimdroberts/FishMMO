using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.Biomes.Editor
{
	/// <summary>
	/// Fills a <see cref="SceneBiomeMap"/> from the biome colour maps the Heightmap tool
	/// exports: one PNG, or a folder of <c>cell_X_Y/cell_X_Y_elevation.png</c> cells stitched
	/// into a single grid. Every pixel becomes the ID of the <see cref="BiomeTemplate"/> whose
	/// <see cref="BiomeTemplate.BiomeColorId"/> is nearest to it — the same matching
	/// PlayableTerrain uses to paint — so the baked map and the terrain agree.
	/// </summary>
	public static class SceneBiomeMapImporter
	{
		/// <summary>Counts and warnings from one import.</summary>
		public sealed class Report
		{
			public int Width;
			public int Height;
			public int Pixels;
			/// <summary>Pixels with no colour (alpha 0), left as no biome.</summary>
			public int Empty;
			/// <summary>Pixels whose nearest template colour was further than <see cref="LooseMatchDistance"/>.</summary>
			public int LooseMatches;
			public readonly Dictionary<BiomeTemplate, int> Counts = new Dictionary<BiomeTemplate, int>();

			public override string ToString()
			{
				return $"{Width}x{Height}: {Pixels - Empty} biome pixel(s), {Empty} empty, {LooseMatches} loose match(es), {Counts.Count} biome(s) used";
			}
		}

		/// <summary>RGB distance beyond which a pixel is still assigned its nearest biome but reported as loose.</summary>
		public const float LooseMatchDistance = 0.08f;

		private static readonly Regex CellFolder = new Regex(@"^cell_(\d+)_(\d+)$", RegexOptions.Compiled);

		/// <summary>Every BiomeTemplate in the project that carries a colour, by path order.</summary>
		public static List<BiomeTemplate> FindColouredTemplates()
		{
			string[] guids = AssetDatabase.FindAssets($"t:{nameof(BiomeTemplate)}");
			var paths = new List<string>(guids.Length);
			foreach (string guid in guids)
			{
				paths.Add(AssetDatabase.GUIDToAssetPath(guid));
			}
			paths.Sort(StringComparer.Ordinal);

			var templates = new List<BiomeTemplate>(paths.Count);
			foreach (string path in paths)
			{
				BiomeTemplate template = AssetDatabase.LoadAssetAtPath<BiomeTemplate>(path);
				if (template != null && template.BiomeColorId.a > 0f)
				{
					templates.Add(template);
				}
			}
			return templates;
		}

		/// <summary>The cached-object ID a template registers under, without needing it loaded through Addressables.</summary>
		public static int IDOf(BiomeTemplate template)
		{
			return template == null ? 0 : (nameof(BiomeTemplate) + template.name).GetDeterministicHashCode();
		}

		/// <summary>The template whose colour is nearest to <paramref name="pixel"/>, and how far it is.</summary>
		public static BiomeTemplate Nearest(Color pixel, IReadOnlyList<BiomeTemplate> templates, out float distance)
		{
			BiomeTemplate best = null;
			distance = float.MaxValue;
			for (int i = 0; i < templates.Count; i++)
			{
				Color c = templates[i].BiomeColorId;
				float dr = c.r - pixel.r, dg = c.g - pixel.g, db = c.b - pixel.b;
				float d = Mathf.Sqrt(dr * dr + dg * dg + db * db);
				if (d < distance)
				{
					distance = d;
					best = templates[i];
				}
			}
			return best;
		}

		/// <summary>
		/// Writes <paramref name="pixels"/> (row-major from the bottom-left, as
		/// <see cref="Texture2D.GetPixels()"/> returns them) into the map, one biome ID per pixel.
		/// The map's world rect is left as authored.
		/// </summary>
		public static Report Import(SceneBiomeMap map, Color[] pixels, int width, int height, IReadOnlyList<BiomeTemplate> templates)
		{
			if (map == null) throw new ArgumentNullException(nameof(map));
			if (pixels == null || pixels.Length != width * height) throw new ArgumentException("Pixel count does not match the size.", nameof(pixels));
			if (templates == null || templates.Count == 0) throw new ArgumentException("No BiomeTemplate carries a colour; nothing can be matched.", nameof(templates));

			var report = new Report { Width = width, Height = height, Pixels = pixels.Length };
			var ids = new int[pixels.Length];
			for (int i = 0; i < pixels.Length; i++)
			{
				Color pixel = pixels[i];
				if (pixel.a <= 0f)
				{
					report.Empty++;
					continue;
				}
				BiomeTemplate biome = Nearest(pixel, templates, out float distance);
				if (distance > LooseMatchDistance)
				{
					report.LooseMatches++;
				}
				ids[i] = IDOf(biome);
				report.Counts.TryGetValue(biome, out int count);
				report.Counts[biome] = count + 1;
			}

			Undo.RecordObject(map, "Import Scene Biome Map");
			map.Width = width;
			map.Height = height;
			map.BiomeIDs = ids;
			EditorUtility.SetDirty(map);
			return report;
		}

		/// <summary>Imports one PNG. The file is decoded directly, so its import settings do not matter.</summary>
		public static Report ImportPng(SceneBiomeMap map, string pngPath)
		{
			Texture2D texture = Decode(pngPath);
			try
			{
				return Import(map, texture.GetPixels(), texture.width, texture.height, FindColouredTemplates());
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(texture);
			}
		}

		/// <summary>
		/// Imports a Heightmap-tool export folder: every <c>cell_X_Y</c> subfolder holding a
		/// <c>cell_X_Y_elevation.png</c> is placed at grid column X, row Y (Y up), and the cells —
		/// which must all share one resolution — are stitched into a single map.
		/// </summary>
		public static Report ImportCellFolder(SceneBiomeMap map, string folder)
		{
			var cells = new Dictionary<(int x, int y), string>();
			int maxX = -1, maxY = -1;
			foreach (string dir in Directory.GetDirectories(folder))
			{
				string name = Path.GetFileName(dir);
				Match m = CellFolder.Match(name);
				if (!m.Success) continue;
				string png = Path.Combine(dir, $"{name}_elevation.png");
				if (!File.Exists(png)) continue;
				int x = int.Parse(m.Groups[1].Value), y = int.Parse(m.Groups[2].Value);
				cells[(x, y)] = png;
				maxX = Mathf.Max(maxX, x);
				maxY = Mathf.Max(maxY, y);
			}
			if (cells.Count == 0)
			{
				throw new FileNotFoundException($"No cell_X_Y/cell_X_Y_elevation.png found under '{folder}'.");
			}

			int cellW = -1, cellH = -1;
			Color[] pixels = null;
			int width = 0, height = 0;
			foreach (KeyValuePair<(int x, int y), string> cell in cells)
			{
				Texture2D texture = Decode(cell.Value);
				try
				{
					if (cellW < 0)
					{
						cellW = texture.width;
						cellH = texture.height;
						width = cellW * (maxX + 1);
						height = cellH * (maxY + 1);
						pixels = new Color[width * height];
					}
					else if (texture.width != cellW || texture.height != cellH)
					{
						throw new InvalidDataException($"Cell '{cell.Value}' is {texture.width}x{texture.height}; expected {cellW}x{cellH} like the others.");
					}
					Color[] cellPixels = texture.GetPixels();
					int ox = cell.Key.x * cellW, oy = cell.Key.y * cellH;
					for (int y = 0; y < cellH; y++)
					{
						Array.Copy(cellPixels, y * cellW, pixels, (oy + y) * width + ox, cellW);
					}
				}
				finally
				{
					UnityEngine.Object.DestroyImmediate(texture);
				}
			}
			return Import(map, pixels, width, height, FindColouredTemplates());
		}

		private static Texture2D Decode(string pngPath)
		{
			var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
			if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(pngPath), false))
			{
				UnityEngine.Object.DestroyImmediate(texture);
				throw new InvalidDataException($"'{pngPath}' is not a readable PNG/JPG.");
			}
			return texture;
		}
	}

	/// <summary>
	/// Inspector for <see cref="SceneBiomeMap"/>: the authored world rect above, import buttons
	/// and a summary of what the grid holds below.
	/// </summary>
	[CustomEditor(typeof(SceneBiomeMap))]
	public class SceneBiomeMapEditor : UnityEditor.Editor
	{
		private string lastReport;

		public override void OnInspectorGUI()
		{
			DrawDefaultInspector();
			var map = (SceneBiomeMap)target;

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Grid", EditorStyles.boldLabel);
			int filled = 0;
			if (map.BiomeIDs != null)
			{
				foreach (int id in map.BiomeIDs)
				{
					if (id != 0) filled++;
				}
			}
			EditorGUILayout.LabelField($"{map.Width} x {map.Height}, {filled} cell(s) with a biome");

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Import", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Pixels are matched to the nearest BiomeTemplate colour (BiomeColorId). Set World Origin and World Size to the terrain the map covers.", MessageType.Info);
			using (new EditorGUILayout.HorizontalScope())
			{
				if (GUILayout.Button("Import PNG…"))
				{
					string path = EditorUtility.OpenFilePanel("Biome colour map", Application.dataPath, "png,jpg");
					if (!string.IsNullOrEmpty(path)) Run(() => SceneBiomeMapImporter.ImportPng(map, path));
				}
				if (GUILayout.Button("Import Heightmap cells…"))
				{
					string path = EditorUtility.OpenFolderPanel("Heightmap export folder (cell_X_Y/…)", Application.dataPath, "");
					if (!string.IsNullOrEmpty(path)) Run(() => SceneBiomeMapImporter.ImportCellFolder(map, path));
				}
			}
			if (!string.IsNullOrEmpty(lastReport))
			{
				EditorGUILayout.HelpBox(lastReport, MessageType.None);
			}
		}

		private void Run(Func<SceneBiomeMapImporter.Report> import)
		{
			try
			{
				SceneBiomeMapImporter.Report report = import();
				var lines = new List<string> { report.ToString() };
				foreach (KeyValuePair<BiomeTemplate, int> pair in report.Counts)
				{
					lines.Add($"  {pair.Key.ResolvedDisplayName}: {pair.Value}");
				}
				lastReport = string.Join("\n", lines);
				Debug.Log($"[SceneBiomeMap] {target.name}: {report}");
			}
			catch (Exception e)
			{
				lastReport = e.Message;
				Debug.LogError($"[SceneBiomeMap] {target.name}: {e.Message}");
			}
		}
	}
}
