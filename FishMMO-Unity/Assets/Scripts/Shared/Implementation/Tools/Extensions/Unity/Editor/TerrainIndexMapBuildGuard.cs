#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>Checks the index-map API contract before a build can discard its alphamap fallback.</summary>
	public static class TerrainIndexMapBuildGuard
	{
		public static string UnsupportedApiReason(IEnumerable<GraphicsDeviceType> apis)
		{
			bool any = false;
			foreach (GraphicsDeviceType api in apis)
			{
				any = true;
				// These client backends provide RG32 uint render targets and unfiltered integer texture loads.
				if (api != GraphicsDeviceType.Direct3D11 && api != GraphicsDeviceType.Direct3D12
					&& api != GraphicsDeviceType.Vulkan && api != GraphicsDeviceType.WebGPU)
					return $"{api} is outside the stored terrain index-map contract (D3D11/12, Vulkan, WebGPU). Retain alphamaps or validate and extend the contract before shipping this backend.";
			}
			return any ? null : "No client graphics API is configured for stored terrain index maps.";
		}

		public static void Validate(Scene scene, BuildTarget target)
		{
			bool arrays = false;
			foreach (GameObject root in scene.GetRootGameObjects())
				foreach (Terrain tile in root.GetComponentsInChildren<Terrain>(true)) arrays |= ClientTerrainLayerStripper.IsArrayTerrain(tile);
			if (!arrays) return;
			string reason = UnsupportedApiReason(PlayerSettings.GetGraphicsAPIs(target));
			if (reason != null) throw new BuildFailedException("[Terrain index] " + reason);
			TerrainArraySet set = AssetDatabase.LoadAssetAtPath<TerrainArraySet>(TerrainArraySet.BakedAssetPath(scene.name));
			if (set == null || set.IndexBakeShader == null || set.IndexBakeShader.name != TerrainIndexMapBaker.ShaderName)
				throw new BuildFailedException($"[Terrain index] '{scene.name}' must ship its terrain array set and index bake/upload shader. Rebuild the scene's terrain arrays before building.");
			var material = new Material(set.IndexBakeShader);
			try
			{
				if (material.FindPass("UploadStored") < 0 || material.FindPass("Bake") < 0)
					throw new BuildFailedException("[Terrain index] Required bake/upload shader pass is missing.");
			}
			finally { Object.DestroyImmediate(material); }
		}
	}
}
#endif
