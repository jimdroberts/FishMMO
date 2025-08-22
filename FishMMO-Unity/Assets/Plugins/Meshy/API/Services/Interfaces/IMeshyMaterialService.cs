#if UNITY_EDITOR
using UnityEngine;

namespace MeshyAI
{
	public interface IMeshyMaterialService
	{
		Material CreateOrLoadMaterial(string assetPath, string modelName, RenderPipeline renderPipeline, float metallic, float smoothness);
		void AssignTexturesToMaterial(Material material, TextureSet textures, RenderPipeline renderPipeline, float metallic, float smoothness);
		TextureSet FindAndImportAllTextures(string modelDirectory, string assetPath, string modelName, bool enableNormal, bool enableRoughness, bool enableMetallic);
		void AssignMaterialToRenderers(GameObject importedObject, Material material);
		void CreatePrefab(GameObject importedObject, string prefabPath);
	}
}
#endif