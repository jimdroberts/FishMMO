using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FishMMO.Shared.WorldDesign;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace FishMMO.TestHarness.World.Editor
{
	/// <summary>
	/// The look arrays' headless check (ProceduralArtLookArrays, 2026-10-28): that the importer reads a flipbook's cells in the
	/// order the generator writes them, a generation of the art the arrays changed, and what was written — each array's layers,
	/// size, format, mips and GPU bytes, and the single maps' bytes they replace.
	/// </summary>
	/// <remarks>
	/// <c>memory/tools/runmethod.sh &lt;out&gt; FishMMO.TestHarness.World.Editor.LookArrayProbe.Run</c> (generate, then report), or
	/// <c>…LookArrayProbe.Report</c> (report only). Lines start "[Look arrays]".
	/// </remarks>
	public static class LookArrayProbe
	{
		public static void Run()
		{
			int code = 1;
			try
			{
				Debug.Log("[Look arrays] flipbook order: " + FlipbookOrder());
				int seed = ProceduralArtCatalogue.DefaultSeed;
				ProceduralArtPayload.State state = ProceduralArtPayload.Check(seed);
				Debug.Log("[Look arrays] before: " + state);
				BiomeArtGenerator.Report report = BiomeArtGenerator.Generate(ProceduralArtMode.Full, seed, BiomeArtGenerator.FamiliesToRebuild(state));
				Debug.Log("[Look arrays] generation: " + report);
				foreach (string problem in report.Problems)
				{
					Debug.Log("[Look arrays] problem: " + problem);
				}
				Debug.Log("[Look arrays] after: " + ProceduralArtPayload.Check(seed));
				Debug.Log(Describe());
				code = report.Completed ? 0 : 1;
			}
			catch (Exception e)
			{
				Debug.LogException(e);
			}
			if (Application.isBatchMode)
			{
				EditorApplication.Exit(code);
			}
		}

		public static void Report()
		{
			Debug.Log("[Look arrays] flipbook order: " + FlipbookOrder());
			Debug.Log(Describe());
			if (Application.isBatchMode)
			{
				EditorApplication.Exit(0);
			}
		}

		/// <summary>
		/// A 3 × 2 flipbook of six flat colours, imported readable and uncompressed as a Texture2DArray with the generator's cell
		/// layout (ProceduralArtLookArrays.CellOrigin): "ok" when slice i reads colour i, else what each slice read.
		/// </summary>
		private static string FlipbookOrder()
		{
			string path = ProceduralArtCatalogue.Root + "/Variants/__LookArrayOrderProbe.png";
			const int size = 8;
			var layout = new ProceduralArtLookArrays.Layout { Size = size, Columns = 3, Rows = 2 };
			var colours = new Color32[6];
			for (int i = 0; i < 6; i++)
			{
				colours[i] = new Color32((byte)(40 * (i + 1)), (byte)(255 - 40 * i), (byte)(i * 17), 255);
			}
			var pixels = new Color32[layout.Width * layout.Height];
			for (int i = 0; i < 6; i++)
			{
				ProceduralArtLookArrays.CellOrigin(layout, i, out int x0, out int y0);
				for (int y = 0; y < size; y++)
				{
					for (int x = 0; x < size; x++)
					{
						pixels[(y0 + y) * layout.Width + x0 + x] = colours[i];
					}
				}
			}
			var texture = new Texture2D(layout.Width, layout.Height, TextureFormat.RGBA32, false, true);
			try
			{
				texture.SetPixels32(pixels);
				File.WriteAllBytes(path, texture.EncodeToPNG());
			}
			finally
			{
				Object.DestroyImmediate(texture);
			}
			try
			{
				AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
				var importer = (TextureImporter)AssetImporter.GetAtPath(path);
				importer.isReadable = true;
				importer.mipmapEnabled = false;
				importer.sRGBTexture = false;
				importer.textureCompression = TextureImporterCompression.Uncompressed;
				importer.npotScale = TextureImporterNPOTScale.None;
				var settings = new TextureImporterSettings();
				importer.ReadTextureSettings(settings);
				settings.textureShape = TextureImporterShape.Texture2DArray;
				settings.flipbookColumns = layout.Columns;
				settings.flipbookRows = layout.Rows;
				importer.SetTextureSettings(settings);
				importer.SaveAndReimport();
				var array = AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
				if (array == null)
				{
					return "the probe did not import as a Texture2DArray";
				}
				var read = new StringBuilder();
				bool ok = array.depth == 6 && array.width == size;
				for (int i = 0; i < Math.Min(6, array.depth); i++)
				{
					Color32 c = array.GetPixels32(i, 0)[size * size / 2];
					ok &= c.r == colours[i].r && c.g == colours[i].g && c.b == colours[i].b;
					read.Append(i).Append('=').Append(c.r).Append('/').Append(c.g).Append('/').Append(c.b).Append(' ');
				}
				return ok ? $"ok (6 slices of {array.width}², {read})" : $"WRONG: depth {array.depth}, width {array.width}, read {read}";
			}
			finally
			{
				AssetDatabase.DeleteAsset(path);
			}
		}

		/// <summary>Every look array on disk: layers, size, format, mips and GPU bytes; and the single maps' bytes they hold.</summary>
		private static string Describe()
		{
			var sb = new StringBuilder("[Look arrays] arrays:");
			long total = 0, singles = 0;
			foreach (ProceduralArtLookArrays.Layout layout in ProceduralArtLookArrays.Layouts)
			{
				var array = AssetDatabase.LoadAssetAtPath<Texture2DArray>(layout.Path);
				long disk = File.Exists(layout.Path) ? new FileInfo(layout.Path).Length : 0;
				if (array == null)
				{
					sb.Append($"\n  {layout.Path}: NOT a Texture2DArray (disk {disk / 1048576.0:0.0} MB)");
					continue;
				}
				long bytes = 0;
				for (int mip = 0; mip < array.mipmapCount; mip++)
				{
					bytes += GraphicsFormatUtility.ComputeMipmapSize(Math.Max(1, array.width >> mip), Math.Max(1, array.height >> mip), array.graphicsFormat);
				}
				long one = bytes;
				bytes *= array.depth;
				total += bytes;
				singles += one * layout.Members.Count;
				// The single map this array's first slice replaced, as it imported (while its old file is on disk): format and mips to compare.
				var single = layout.Members.Count > 0 ? AssetDatabase.LoadAssetAtPath<Texture2D>(layout.Members[0]) : null;
				string was = single != null ? $"; single was {single.graphicsFormat}, {single.mipmapCount} mips, {single.filterMode}/{single.wrapMode}/aniso {single.anisoLevel}" : string.Empty;
				sb.Append($"\n  {Path.GetFileName(layout.Path)}: {array.depth} layers ({layout.Members.Count} maps, {layout.Columns}x{layout.Rows}) of {array.width}x{array.height} {array.graphicsFormat}, "
					+ $"{array.mipmapCount} mips, {array.filterMode}/{array.wrapMode}/aniso {array.anisoLevel}, GPU {bytes / 1048576.0:0.0} MB, PNG {disk / 1048576.0:0.0} MB{was}");
			}
			sb.Append($"\n  total GPU {total / 1048576.0:0.0} MB for maps whose single textures were {singles / 1048576.0:0.0} MB together");
			// The single maps still on disk (old payload, now unreferenced): what a scene could still load if anything named them.
			long left = 0;
			int count = 0;
			foreach (ProceduralArtLookArrays.Layout layout in ProceduralArtLookArrays.Layouts)
			{
				foreach (string member in layout.Members)
				{
					if (File.Exists(member))
					{
						count++;
						left += new FileInfo(member).Length;
					}
				}
			}
			var singlesOnDisk = new HashSet<string>(StringComparer.Ordinal);
			foreach (ProceduralArtLookArrays.Layout layout in ProceduralArtLookArrays.Layouts)
			{
				foreach (string member in layout.Members)
				{
					if (File.Exists(member))
					{
						singlesOnDisk.Add(member);
					}
				}
			}
			var users = new List<string>();
			foreach (string guid in AssetDatabase.FindAssets("t:Material"))
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				foreach (string dependency in AssetDatabase.GetDependencies(path, false))
				{
					if (singlesOnDisk.Contains(dependency))
					{
						users.Add(path);
						break;
					}
				}
			}
			sb.Append($"\n  old single maps on disk: {count} files, {left / 1048576.0:0.0} MB of PNG; assets with materials still naming one: {users.Count}");
			for (int i = 0; i < Math.Min(20, users.Count); i++)
			{
				sb.Append("\n    ").Append(users[i]);
			}
			return sb.ToString();
		}
	}
}
