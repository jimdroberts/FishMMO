#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Import settings for the generated textures, chosen by file name on every import.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Every import, not only the first.</b> The textures are payload: gitignored, regenerated on
	/// each machine, their .meta written by the generator with nothing in it but the GUID
	/// (<see cref="ProceduralArtPayload"/>). Settings changed by hand would live only in one
	/// machine's untracked .meta and be lost with the next wipe, so the settings are code, here, and
	/// every clone imports the payload identically. To change them, change this file and raise
	/// <see cref="GetVersion"/>; Unity then reimports every texture this processes.
	/// </para>
	/// <para>
	/// <b>The shape is set, never inherited (2026-10-02).</b> The generator writes each PNG's .meta
	/// with nothing but its GUID (<see cref="ProceduralArtPayload.MetaText"/>). A TextureImporter read
	/// from a .meta with no importer block has no <c>serializedVersion</c>, so Unity runs its oldest
	/// upgrade path on it, and that path derives the shape from the legacy cube-map setting, whose
	/// default is "auto": every payload PNG imported as a <b>Cubemap</b> (the .metas read
	/// <c>textureShape: 2</c>, against <c>1</c> on every other texture in the project).
	/// <c>LoadAssetAtPath&lt;Texture2D&gt;</c> then answers null for a file that is plainly there, and
	/// every material and terrain layer was written with empty slots — grey slab trees, flat ground.
	/// So the shape, like every other setting here, is code: <see cref="TextureImporterShape.Texture2D"/>
	/// on every import, and version 3 reimports what version 2 imported as cubes.
	/// </para>
	/// <para>
	/// <b>sRGB only for colour.</b> Albedo is authored and stored in sRGB. Normal maps go through
	/// the normal-map importer (linear, unpacked by the shader). Masks are data — metallic,
	/// occlusion, height, smoothness — and sampled as sRGB they would read every value wrong by
	/// the gamma curve; the terrain's smoothness and occlusion would be visibly off.
	/// </para>
	/// <para>
	/// <b>Cutout foliage keeps its coverage.</b> The atlas and billboards preserve alpha coverage
	/// in their mipmaps at the cutoff the vegetation shader uses, so a tree does not go bald with
	/// distance, and alpha-is-transparency bleeds colour into the cut-away texels so the edges do
	/// not darken as they filter.
	/// </para>
	/// </remarks>
	public sealed class ProceduralArtTextureImport : AssetPostprocessor
	{
		public override uint GetVersion() => 3;

		private void OnPreprocessTexture()
		{
			if (!assetPath.StartsWith(ProceduralArtCatalogue.TexturesFolder + "/", System.StringComparison.Ordinal))
			{
				return;
			}
			var importer = (TextureImporter)assetImporter;
			string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
			if (ProceduralArtLookArrays.TryParseArrayName(file, out string set, out ProceduralArtLookArrays.Kind kind))
			{
				LookArray(importer, set, kind);
				return;
			}
			// Never inherited from the .meta's legacy upgrade, which makes a cube map (see the remarks).
			importer.textureShape = TextureImporterShape.Texture2D;
			importer.mipmapEnabled = true;
			importer.anisoLevel = 8;
			importer.filterMode = FilterMode.Trilinear;
			importer.textureCompression = TextureImporterCompression.CompressedHQ;
			importer.isReadable = false;

			if (file.EndsWith("_Normal", System.StringComparison.Ordinal))
			{
				importer.textureType = TextureImporterType.NormalMap;
				importer.wrapMode = TextureWrapMode.Repeat;
			}
			else if (file.EndsWith("_Mask", System.StringComparison.Ordinal))
			{
				importer.textureType = TextureImporterType.Default;
				importer.sRGBTexture = false;
				importer.alphaSource = TextureImporterAlphaSource.FromInput;
				importer.wrapMode = TextureWrapMode.Repeat;
			}
			// The vine atlas is a leaf atlas like the foliage one: cut out at the same cutoff, its coverage kept down the mips.
			else if (file.StartsWith("Billboard_", System.StringComparison.Ordinal) || file == ProceduralArtCatalogue.AtlasName || file == ProceduralArtCatalogue.VineAtlasName)
			{
				importer.textureType = TextureImporterType.Default;
				importer.sRGBTexture = true;
				importer.alphaSource = TextureImporterAlphaSource.FromInput;
				importer.alphaIsTransparency = true;
				importer.mipMapsPreserveCoverage = true;
				importer.alphaTestReferenceValue = 0.5f;
				importer.wrapMode = TextureWrapMode.Clamp;
				importer.anisoLevel = 4;
			}
			else
			{
				// Albedo: colour in RGB, smoothness in A (the terrain reads it there without a mask).
				importer.textureType = TextureImporterType.Default;
				importer.sRGBTexture = true;
				importer.alphaSource = TextureImporterAlphaSource.FromInput;
				importer.alphaIsTransparency = false;
				importer.wrapMode = TextureWrapMode.Repeat;
			}
		}

		/// <summary>
		/// A look array's flipbook (<see cref="ProceduralArtLookArrays"/>, 2026-10-28): a <see cref="Texture2DArray"/> of its cells, each
		/// imported exactly as the single map it replaces was above — colour as an albedo (sRGB, its alpha the smoothness), normals
		/// through the normal-map importer, masks linear; mips, trilinear, aniso 8, Repeat, high-quality compression. The grid is
		/// read from the image's own size and the set's slice size; the image is never scaled (no power-of-two rounding, and a
		/// max size the largest flipbook fits), so every cell is one slice at the single map's size.
		/// </summary>
		private void LookArray(TextureImporter importer, string set, ProceduralArtLookArrays.Kind kind)
		{
			int size = ProceduralArtLookArrays.SizeOf(set);
			if (size <= 0 || !PngSize(assetPath, out int width, out int height) || width % size != 0 || height % size != 0)
			{
				Debug.LogError($"[Biome art] {assetPath}: not a look array flipbook of its set's {size}² cells; imported as it is.");
				return;
			}
			importer.mipmapEnabled = true;
			importer.anisoLevel = 8;
			importer.filterMode = FilterMode.Trilinear;
			importer.textureCompression = TextureImporterCompression.CompressedHQ;
			importer.isReadable = false;
			importer.wrapMode = TextureWrapMode.Repeat;
			importer.npotScale = TextureImporterNPOTScale.None;
			importer.maxTextureSize = ProceduralArtLookArrays.MaxSourceSide;
			importer.alphaIsTransparency = false;
			switch (kind)
			{
				case ProceduralArtLookArrays.Kind.Normal:
					importer.textureType = TextureImporterType.NormalMap;
					break;
				case ProceduralArtLookArrays.Kind.Mask:
					importer.textureType = TextureImporterType.Default;
					importer.sRGBTexture = false;
					importer.alphaSource = TextureImporterAlphaSource.FromInput;
					break;
				default:
					importer.textureType = TextureImporterType.Default;
					importer.sRGBTexture = true;
					importer.alphaSource = TextureImporterAlphaSource.FromInput;
					break;
			}
			var settings = new TextureImporterSettings();
			importer.ReadTextureSettings(settings);
			settings.textureShape = TextureImporterShape.Texture2DArray;
			settings.flipbookColumns = width / size;
			settings.flipbookRows = height / size;
			importer.SetTextureSettings(settings);
		}

		/// <summary>A PNG's size from its header (the IHDR chunk), read before the importer has opened it.</summary>
		private static bool PngSize(string path, out int width, out int height)
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
	}
}
#endif
