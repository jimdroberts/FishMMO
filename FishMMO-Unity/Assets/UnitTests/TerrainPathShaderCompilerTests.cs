using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace FishMMO.UnitTests
{
	public class TerrainPathShaderCompilerTests
	{
		[Test]
		public void TerrainArrayPathBlendCompilesWithWebGpuOptimizationEnabled()
		{
			if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
				Assert.Ignore("WebGL build support is required for this compiler regression test.");
			var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Prefabs/Client/Weather/Shaders/FishWeatherTerrainArray.shader");
			Assert.That(shader, Is.Not.Null);
			var pass = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0);
			var result = pass.CompileVariant(ShaderType.Vertex, new[] { "_CLUSTER_LIGHT_LOOP" }, ShaderCompilerPlatform.WebGPU, BuildTarget.WebGL);
			Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Messages.Select(message => message.message)));
			Assert.That(result.Messages.Any(message => message.severity == ShaderCompilerMessageSeverity.Error), Is.False);
			Assert.That(result.ShaderData.Length, Is.GreaterThan(0));
		}
	}
}
