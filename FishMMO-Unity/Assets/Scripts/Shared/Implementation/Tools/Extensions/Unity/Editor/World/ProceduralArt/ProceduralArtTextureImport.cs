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
			else if (file.StartsWith("Billboard_", System.StringComparison.Ordinal) || file == ProceduralArtCatalogue.AtlasName)
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
	}
}
#endif
