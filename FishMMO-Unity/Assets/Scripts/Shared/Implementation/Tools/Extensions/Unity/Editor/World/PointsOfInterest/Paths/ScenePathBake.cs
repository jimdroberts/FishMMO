#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Bakes a scene's ways into the cut: the path field (<see cref="ScenePathField"/>) written beside the terrain as two
	/// linear, unfiltered-by-import PNGs, and the client-only <see cref="ScenePathSurfaceBinder"/> object that binds them.
	/// </summary>
	/// <remarks>
	/// PNG rather than a texture asset: the field is smooth and mostly empty, so it compresses several times over and a
	/// re-cut's diff is one binary file. Imported uncompressed (RGBA32), no mips, no sRGB, clamped: the shader decodes the
	/// bytes as numbers.
	/// </remarks>
	public static class ScenePathBake
	{
		public static string TablePath(string terrainFolder, string sceneName) => $"{terrainFolder}/{WorldEditorAssets.Sanitize(sceneName)} Path Table.png";
		public static string AtlasPath(string terrainFolder, string sceneName) => $"{terrainFolder}/{WorldEditorAssets.Sanitize(sceneName)} Path Atlas.png";

		/// <summary>Bakes the asset's paths and binds them under the scene's points-of-interest root.</summary>
		public static void Write(Scene scene, Transform root, string terrainFolder, string sceneName, ScenePointsOfInterest asset,
			List<string> wrote, List<string> notes)
		{
			ScenePathField field = ScenePathField.Build(asset != null ? asset.Paths : null);
			Transform existing = root != null ? root.Find(ScenePathSurfaceBinder.ObjectName) : null;
			if (field.PageCount == 0)
			{
				if (existing != null)
				{
					Object.DestroyImmediate(existing.gameObject);
				}
				ScenePathSurfaceBinder.Publish();
				return;
			}
			WorldEditorAssets.EnsureFolder(terrainFolder);
			string tablePath = TablePath(terrainFolder, sceneName), atlasPath = AtlasPath(terrainFolder, sceneName);
			Texture2D table = WritePng(tablePath, field.TableWidth, field.TableDepth, field.Table, FilterMode.Point);
			Texture2D atlas = WritePng(atlasPath, field.AtlasTexels, field.AtlasTexels, field.Atlas, FilterMode.Bilinear);
			wrote?.Add(tablePath);
			wrote?.Add(atlasPath);

			GameObject host = existing != null ? existing.gameObject : new GameObject(ScenePathSurfaceBinder.ObjectName);
			if (existing == null)
			{
				host.transform.SetParent(root, false);
			}
			if (!host.TryGetComponent(out ClientOnlyObject _))
			{
				host.AddComponent<ClientOnlyObject>();
			}
			if (!host.TryGetComponent(out ScenePathSurfaceBinder binder))
			{
				binder = host.AddComponent<ScenePathSurfaceBinder>();
			}
			binder.Points = asset;
			binder.Table = table;
			binder.Atlas = atlas;
			binder.Parameters = new Vector4(field.Origin.x, field.Origin.y, 1f / ScenePathField.PageMetres, 1f);
			binder.Size = new Vector4(field.TableWidth, field.TableDepth, 1f / field.AtlasTexels, ScenePathField.StoredTexels);
			EditorUtility.SetDirty(binder);
			ScenePathSurfaceBinder.Publish();
			TerrainArrayBinder.BindAllLoaded();

			int kb = Mathf.RoundToInt(field.AtlasTexels * field.AtlasTexels * 4 / 1024f);
			notes?.Add($"Paths: baked {field.PageCount:N0} page(s) of 16 m into a {field.AtlasTexels}² atlas ({kb:N0} KB on the GPU)" +
				(field.Dropped > 0 ? $"; {field.Dropped} page(s) did not fit and are not drawn" : string.Empty) + ".");
		}

		private static Texture2D WritePng(string path, int width, int height, Color32[] pixels, FilterMode filter)
		{
			var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
			texture.SetPixels32(pixels);
			texture.Apply(false, false);
			File.WriteAllBytes(path, texture.EncodeToPNG());
			Object.DestroyImmediate(texture);
			AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
			if (AssetImporter.GetAtPath(path) is TextureImporter importer)
			{
				importer.textureType = TextureImporterType.Default;
				importer.sRGBTexture = false;
				importer.mipmapEnabled = false;
				importer.alphaSource = TextureImporterAlphaSource.FromInput;
				importer.alphaIsTransparency = false;
				importer.npotScale = TextureImporterNPOTScale.None;
				importer.isReadable = false;
				importer.wrapMode = TextureWrapMode.Clamp;
				importer.filterMode = filter;
				importer.textureCompression = TextureImporterCompression.Uncompressed;
				// Uncompressed RGBA input imports as RGBA32 on every platform.
				importer.maxTextureSize = 8192;
				importer.SaveAndReimport();
			}
			return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
		}
	}
}
#endif
