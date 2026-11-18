#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Web builds import the large baked texture arrays (terrain arrays, look arrays) at half their slice size.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why.</b> A web player is 32-bit WebAssembly: everything a world loads shares one heap of at most 4 GB, and a
	/// world's bundles stay resident in it beside what is loaded from them. Moron 2 ran out of memory loading, with its
	/// terrain arrays and prop look arrays among the largest objects in its bundles. Half the slice size is a quarter of
	/// the texels: those arrays cost about a quarter on the web, and desktop builds are unchanged.
	/// </para>
	/// <para>
	/// <b>Max Size is per slice for a flipbook array.</b> Measured on Unity 6000.4 (an empty project, 2026-11-07): a
	/// 3×1 flipbook of 128-pixel cells imported with a WebGL Max Size of 256 stayed 128 a slice, and at 64 became 64 a
	/// slice, every cell in its own place. So the override is half the cell, whatever the grid's width; a limit on the
	/// whole sheet would have cut cells apart on a grid that is not a power of two wide (9 cells of 1024 = 9216).
	/// </para>
	/// <para>
	/// Set on every import by the postprocessors that own these files (TerrainArrayTextureImporter,
	/// ProceduralArtTextureImport), never by hand: the files are build output, regenerated on every machine.
	/// </para>
	/// </remarks>
	public static class WebTextureBudget
	{
		/// <summary>The platform name the importer's per-platform settings use for web builds.</summary>
		public const string WebPlatform = "WebGL";

		/// <summary>Each slice is divided by this on the web.</summary>
		public const int SliceDivisor = 2;

		/// <summary>The smallest Max Size the importer accepts.</summary>
		private const int SmallestMaxSize = 32;

		/// <summary>The web Max Size for a flipbook of <paramref name="sliceSize"/>-pixel cells: half the cell, a power of two.</summary>
		public static int WebSliceLimit(int sliceSize)
		{
			int half = Mathf.Max(SmallestMaxSize, sliceSize / SliceDivisor);
			int limit = SmallestMaxSize;
			while (limit * 2 <= half)
			{
				limit *= 2;
			}
			return limit;
		}

		/// <summary>A PNG's size from its header (the IHDR chunk), readable before the importer has opened the file.</summary>
		public static bool TryReadPngSize(string path, out int width, out int height)
		{
			width = height = 0;
			try
			{
				using (var stream = System.IO.File.OpenRead(path))
				{
					var header = new byte[24];
					if (stream.Read(header, 0, 24) < 24 || header[1] != (byte)'P' || header[2] != (byte)'N' || header[3] != (byte)'G')
					{
						return false;
					}
					width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
					height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
					return width > 0 && height > 0;
				}
			}
			catch (System.IO.IOException)
			{
				return false;
			}
		}

		/// <summary>
		/// Gives <paramref name="importer"/> a web override that imports each slice at <see cref="WebSliceLimit"/>. The
		/// compression is carried from the default platform, so the web format is chosen as it was before; an override that
		/// already names a format keeps it.
		/// </summary>
		public static void LimitWebSlices(TextureImporter importer, int sliceSize)
		{
			if (importer == null || sliceSize <= 0)
			{
				return;
			}
			TextureImporterPlatformSettings defaults = importer.GetDefaultPlatformTextureSettings();
			TextureImporterPlatformSettings web = importer.GetPlatformTextureSettings(WebPlatform);
			if (!web.overridden)
			{
				web.format = TextureImporterFormat.Automatic;
				web.textureCompression = defaults.textureCompression;
				web.compressionQuality = defaults.compressionQuality;
				web.crunchedCompression = defaults.crunchedCompression;
				web.resizeAlgorithm = defaults.resizeAlgorithm;
			}
			web.overridden = true;
			web.maxTextureSize = WebSliceLimit(sliceSize);
			importer.SetPlatformTextureSettings(web);
		}
	}
}
#endif
