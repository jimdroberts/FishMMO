#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.CustomBuildTool.Core;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared.CustomBuildTool.Addressables
{
	/// <summary>
	/// SBP invokes these callbacks for Addressables as well as player builds. Never use
	/// UNITY_SERVER here: bundle building temporarily switches the Editor to Player.
	/// </summary>
	public sealed class ServerContentBuildProcessor : IPreprocessShaders, IPreprocessComputeShaders, IProcessSceneWithReport
	{
		public int callbackOrder => int.MaxValue - 1;

		public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> variants)
		{
			if (ServerContentBuildScope.ShouldStripRendering) variants.Clear();
		}

		public void OnProcessComputeShader(ComputeShader shader, string kernel, IList<ShaderCompilerData> variants)
		{
			if (ServerContentBuildScope.ShouldStripRendering) variants.Clear();
		}

		public void OnProcessScene(Scene scene, BuildReport report)
		{
			if (!ServerContentBuildScope.ShouldStripRendering) return;
			// These are build-scene instances. Do not edit shared prefab/material assets,
			// TerrainData, MeshFilter meshes or any collider or gameplay components.
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
					renderer.sharedMaterials = Array.Empty<Material>();
				foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true))
					terrain.materialTemplate = null;
				foreach (Light light in root.GetComponentsInChildren<Light>(true))
					light.cookie = null;
			}
		}
	}
}
#endif
