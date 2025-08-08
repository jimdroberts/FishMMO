using UnityEngine;
using UnityEditor;
using System.IO;

namespace MeshyAI
{
	public class MeshyMaterialService : IMeshyMaterialService
	{
		public Material CreateOrLoadMaterial(string assetPath, string modelName, RenderPipeline renderPipeline, float metallic, float smoothness)
		{
			Shader urpLitShader = Shader.Find("Universal Render Pipeline/Lit");
			Shader targetShader = renderPipeline == RenderPipeline.URP && urpLitShader != null ? urpLitShader : Shader.Find("Standard");
			string materialPath = Path.Combine(assetPath, $"{modelName}_Mat.mat");
			materialPath = materialPath.Replace('\\', '/');

			Material newMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
			if (newMaterial == null)
			{
				newMaterial = new Material(targetShader);
				AssetDatabase.CreateAsset(newMaterial, materialPath);
			}
			else if (newMaterial.shader != targetShader)
			{
				newMaterial.shader = targetShader;
			}

			// Set default values
			if (newMaterial.HasFloat("_Metallic")) newMaterial.SetFloat("_Metallic", metallic);
			if (newMaterial.HasFloat("_Smoothness")) newMaterial.SetFloat("_Smoothness", smoothness);

			return newMaterial;
		}

		public void AssignTexturesToMaterial(Material material, TextureSet textures, RenderPipeline renderPipeline, float metallic, float smoothness)
		{
			// Assign base color
			if (textures.baseColor != null)
			{
				if (renderPipeline == RenderPipeline.URP)
				{
					material.SetTexture("_BaseMap", textures.baseColor);
				}
				else // Standard
				{
					material.SetTexture("_MainTex", textures.baseColor);
				}
			}

			// Assign normal map
			if (textures.normal != null)
			{
				if (renderPipeline == RenderPipeline.URP)
				{
					material.SetTexture("_NormalMap", textures.normal);
				}
				else // Standard
				{
					material.SetTexture("_BumpMap", textures.normal);
				}
				material.EnableKeyword("_NORMALMAP");
			}

			// Assign metallic/roughness based on pipeline
			if (renderPipeline == RenderPipeline.URP)
			{
				// URP uses a packed map (metallic in R, smoothness in A)
				Texture2D packedMetallicSmoothness = PackMetallicSmoothnessMap(textures.metallic, textures.roughness);
				if (packedMetallicSmoothness != null)
				{
					material.SetTexture("_MetallicGlossMap", packedMetallicSmoothness);
					material.EnableKeyword("_METALLICGLOSSMAP");
					material.SetOverrideTag("RenderType", "Opaque");
					material.SetInt("_WorkflowMode", 1); // 1 = Metallic
				}
			}
			else // Standard
			{
				if (textures.metallic != null)
				{
					material.SetTexture("_MetallicGlossMap", textures.metallic);
				}
				if (textures.roughness != null)
				{
					// For standard shader, we can use the roughness map directly as smoothness is 1-roughness
					Texture2D smoothnessTex = ConvertRoughnessToSmoothness(textures.roughness);
					material.SetTexture("_GlossMap", smoothnessTex);
				}
			}
		}

		public TextureSet FindAndImportAllTextures(string modelDirectory, string assetPath, string modelName, bool enableNormal, bool enableRoughness, bool enableMetallic)
		{
			var textures = new TextureSet();

			string modelDirectoryAbs = Path.GetFullPath(modelDirectory);

			textures.baseColor = (Texture2D)AssetDatabase.LoadAssetAtPath(FindTextureAssetPath(modelDirectoryAbs, assetPath, modelName + "_BaseColor"), typeof(Texture2D));
			if (enableNormal) textures.normal = (Texture2D)AssetDatabase.LoadAssetAtPath(FindTextureAssetPath(modelDirectoryAbs, assetPath, modelName + "_Normal"), typeof(Texture2D));
			if (enableRoughness) textures.roughness = (Texture2D)AssetDatabase.LoadAssetAtPath(FindTextureAssetPath(modelDirectoryAbs, assetPath, modelName + "_Roughness"), typeof(Texture2D));
			if (enableMetallic) textures.metallic = (Texture2D)AssetDatabase.LoadAssetAtPath(FindTextureAssetPath(modelDirectoryAbs, assetPath, modelName + "_Metallic"), typeof(Texture2D));

			return textures;
		}

		public void AssignMaterialToRenderers(GameObject importedObject, Material material)
		{
			Renderer[] renderers = importedObject.GetComponentsInChildren<Renderer>();
			foreach (var renderer in renderers)
			{
				var mats = renderer.sharedMaterials;
				for (int i = 0; i < mats.Length; i++) mats[i] = material;
				renderer.sharedMaterials = mats;
			}
		}

		public void CreatePrefab(GameObject importedObject, string prefabPath)
		{
			PrefabUtility.SaveAsPrefabAsset(importedObject, prefabPath);
			AssetDatabase.Refresh();
		}

		private static string FindTextureAssetPath(string modelDirectoryAbs, string assetDir, string baseNameWithSuffix)
		{
			string[] ImageExts = new[] { ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".bmp", ".psd", ".exr" };
			foreach (var ext in ImageExts)
			{
				string abs = Path.Combine(modelDirectoryAbs, baseNameWithSuffix + ext);
				if (File.Exists(abs))
				{
					string rel = (assetDir.TrimEnd('/') + "/" + baseNameWithSuffix + ext).Replace('\\', '/');
					return rel;
				}
			}
			return null;
		}

		private static Texture2D PackMetallicSmoothnessMap(Texture2D metallicMap, Texture2D roughnessMap)
		{
			if (metallicMap == null && roughnessMap == null) return null;

			// Get the largest dimensions from the available textures
			int width = 0, height = 0;
			if (metallicMap != null) { width = metallicMap.width; height = metallicMap.height; }
			if (roughnessMap != null)
			{
				if (roughnessMap.width > width) width = roughnessMap.width;
				if (roughnessMap.height > height) height = roughnessMap.height;
			}

			// If both maps are null, we have nothing to pack
			if (width == 0 || height == 0) return null;

			bool restoreMetalReadable = metallicMap != null && !metallicMap.isReadable;
			bool restoreRoughReadable = roughnessMap != null && !roughnessMap.isReadable;

			// Ensure textures are readable
			if (metallicMap != null) SetTextureReadable(metallicMap, true);
			if (roughnessMap != null) SetTextureReadable(roughnessMap, true);

			Texture2D packedTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
			Color32[] metallicPixels = metallicMap != null ? metallicMap.GetPixels32() : null;
			Color32[] roughnessPixels = roughnessMap != null ? roughnessMap.GetPixels32() : null;

			Color32[] packedPixels = new Color32[width * height];
			for (int i = 0; i < packedPixels.Length; i++)
			{
				byte metallic = metallicPixels != null ? metallicPixels[i].r : (byte)0;
				// Smoothness = 1 - Roughness. We need to convert the byte range
				byte smoothness = roughnessPixels != null ? (byte)(255 - roughnessPixels[i].r) : (byte)255;
				packedPixels[i] = new Color32(metallic, 0, 0, smoothness);
			}

			packedTex.SetPixels32(packedPixels);
			packedTex.Apply();

			// Restore readability if it was changed
			if (restoreMetalReadable && metallicMap != null) SetTextureReadable(metallicMap, false);
			if (restoreRoughReadable && roughnessMap != null) SetTextureReadable(roughnessMap, false);

			return packedTex;
		}

		private static Texture2D ConvertRoughnessToSmoothness(Texture2D roughnessMap)
		{
			if (roughnessMap == null) return null;

			bool restoreRoughReadable = !roughnessMap.isReadable;
			SetTextureReadable(roughnessMap, true);

			Texture2D smoothnessTex = new Texture2D(roughnessMap.width, roughnessMap.height, TextureFormat.Alpha8, false);
			Color32[] roughnessPixels = roughnessMap.GetPixels32();
			Color32[] smoothnessPixels = new Color32[roughnessPixels.Length];

			for (int i = 0; i < roughnessPixels.Length; i++)
			{
				smoothnessPixels[i].a = (byte)(255 - roughnessPixels[i].r);
			}

			smoothnessTex.SetPixels32(smoothnessPixels);
			smoothnessTex.Apply();

			if (restoreRoughReadable) SetTextureReadable(roughnessMap, false);

			return smoothnessTex;
		}

		private static void SetTextureReadable(Texture2D texture, bool isReadable)
		{
			string assetPath = AssetDatabase.GetAssetPath(texture);
			if (string.IsNullOrEmpty(assetPath)) return;

			TextureImporter ti = AssetImporter.GetAtPath(assetPath) as TextureImporter;
			if (ti == null) return;

			ti.isReadable = isReadable;
			ti.SaveAndReimport();
		}
	}
}