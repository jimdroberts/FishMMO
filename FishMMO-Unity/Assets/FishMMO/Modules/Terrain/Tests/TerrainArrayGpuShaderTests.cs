using System;
using System.Linq;
using System.Runtime.InteropServices;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>Split from TerrainIndexFastTests (Phase 4 of the core and modules plan, 2026-11-18): the Terrain module's half.</summary>
	public class TerrainArrayGpuShaderTests
	{
		[TestCase(ShaderCompilerPlatform.Vulkan,BuildTarget.StandaloneLinux64)]
		[TestCase(ShaderCompilerPlatform.D3D,BuildTarget.StandaloneWindows64)]
		[TestCase(ShaderCompilerPlatform.WebGPU,BuildTarget.WebGL)]
		public void FastTerrainShaderCompiles(ShaderCompilerPlatform platform,BuildTarget target)
		{
			var shader = AssetDatabase.LoadAssetAtPath<Shader>(ProjectFiles.Path("Assets/FishMMO/Modules/Terrain/Shaders/FishWeatherTerrainArrayGpu.shader"));
			foreach (var stage in new[] { ShaderType.Vertex,ShaderType.Fragment })
			{
				var result = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0).CompileVariant(stage,new[] {"_CLUSTER_LIGHT_LOOP","_MAIN_LIGHT_SHADOWS_CASCADE","_SHADOWS_SOFT"},platform,target);
				Assert.That(result.Success,Is.True,string.Join("\n",result.Messages.Select(m=>m.message))); Assert.That(result.Messages.Any(m=>m.severity==ShaderCompilerMessageSeverity.Error),Is.False);
			}
		}
	}
}
