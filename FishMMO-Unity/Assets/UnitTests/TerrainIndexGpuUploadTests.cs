using System;
using System.Linq;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	public class TerrainIndexGpuUploadTests
	{
		private const string ShaderPath = "Assets/Prefabs/Client/Weather/Shaders/FishTerrainIndexBake.shader";
		private static uint[] ReadWords(Texture index, int size)
		{
			var compute = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/TestHarness/World/Editor/TerrainIndexLoadProbe.compute"));
			try
			{
				using var words = new GraphicsBuffer(GraphicsBuffer.Target.Structured, size * size, 8);
				compute.SetTexture(0, "_Index", index); compute.SetBuffer(0, "_Words", words); compute.SetInt("_Width", size);
				compute.Dispatch(0, (size + 7) / 8, (size + 7) / 8, 1);
				var result = new uint[size * size * 2]; words.GetData(result); return result;
			}
			finally { Object.DestroyImmediate(compute); }
		}

		[Test]
		public void StoredWordsSurviveGpuUploadAndIntegerLoadWithNoAlphamaps()
		{
			if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsComputeShaders) Assert.Ignore("Requires a real GPU");
			Assert.That(TerrainIndexMapBaker.CanUpload(), Is.True);
			const int size = 256;
			var map = new uint[size * size * 2];
			for (uint i = 0; i < size * size; i++)
			{
				// Every low/high byte combination, sign bits, zero and all-one words; asymmetric rows expose flips.
				map[2 * i] = i | ((65535u - i) << 16);
				map[2 * i + 1] = unchecked(i * 2654435761u);
			}
			map[0] = 0; map[1] = uint.MaxValue; map[2] = 0x80000000; map[3] = 0x7fc00000;
			var stripped = new TerrainData();
			RenderTexture texture = null;
			try
			{
				Assert.That(stripped.alphamapTextureCount, Is.Zero);
				texture = TerrainIndexMapBaker.Upload(new TerrainDetailBake.IndexMap { Resolution = size, Map = map }, "Stored map GPU test");
				Assert.That(texture, Is.Not.Null);
				Assert.That(texture.graphicsFormat, Is.EqualTo(GraphicsFormat.R32G32_UInt));
				CollectionAssert.AreEqual(map, ReadWords(texture, size));
			}
			finally { if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); } Object.DestroyImmediate(stripped); }
		}

		[Test]
		public void LiveBakeAndStoredUploadProduceTheSameGpuWords()
		{
			if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsComputeShaders) Assert.Ignore("Requires a real GPU");
			var data = new TerrainData { alphamapResolution = 16 };
			var layers = Enumerable.Range(0, 7).Select(_ => new TerrainLayer()).ToArray();
			RenderTexture stored = null, live = null;
			try
			{
				data.terrainLayers = layers;
				var paint = new float[16, 16, layers.Length];
				for (int z = 0; z < 16; z++) for (int x = 0; x < 16; x++)
				{
					paint[z, x, (x + z) % 7] = 0.7f; paint[z, x, (x + z + 2) % 7] = 0.3f;
				}
				data.SetAlphamaps(0, 0, paint);
				uint[] expected = TerrainIndexMap.Bake(data.GetAlphamaps(0, 0, 16, 16));
				stored = TerrainIndexMapBaker.Upload(new TerrainDetailBake.IndexMap { Resolution = 16, Map = expected }, "Stored comparison");
				live = TerrainIndexMapBaker.Create(16, 16, 1, "Live comparison");
				Assert.That(TerrainIndexMapBaker.Bake(data, Shader.Find(TerrainIndexMapBaker.ShaderName), live), Is.True);
				CollectionAssert.AreEqual(expected, ReadWords(stored, 16));
				CollectionAssert.AreEqual(expected, ReadWords(live, 16));
			}
			finally
			{
				foreach (RenderTexture texture in new[] { stored, live }) if (texture != null) { texture.Release(); Object.DestroyImmediate(texture); }
				Object.DestroyImmediate(data); foreach (TerrainLayer layer in layers) Object.DestroyImmediate(layer);
				TerrainIndexMapBaker.ReleaseScratch();
			}
		}

		[TestCase(GraphicsDeviceType.Direct3D11)]
		[TestCase(GraphicsDeviceType.Direct3D12)]
		[TestCase(GraphicsDeviceType.Vulkan)]
		[TestCase(GraphicsDeviceType.WebGPU)]
		public void BuildContractAcceptsClientBackends(GraphicsDeviceType api)
		{
			Assert.That(TerrainIndexMapBuildGuard.UnsupportedApiReason(new[] { api }), Is.Null);
		}

		[Test]
		public void BuildContractRejectsUnvalidatedFallbacksAndMissingApis()
		{
			Assert.That(TerrainIndexMapBuildGuard.UnsupportedApiReason(new[] { GraphicsDeviceType.WebGPU, GraphicsDeviceType.OpenGLES3 }), Does.Contain("OpenGLES3"));
			Assert.That(TerrainIndexMapBuildGuard.UnsupportedApiReason(Array.Empty<GraphicsDeviceType>()), Is.Not.Null);
		}

		[TestCase(ShaderCompilerPlatform.Vulkan, BuildTarget.StandaloneLinux64)]
		[TestCase(ShaderCompilerPlatform.WebGPU, BuildTarget.WebGL)]
		[TestCase(ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64)]
		public void BakeAndStoredUploadCompileForClientBackends(ShaderCompilerPlatform platform, BuildTarget target)
		{
			Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
			for (int p = 0; p < 2; p++) foreach (ShaderType stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
			{
				var result = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(p).CompileVariant(stage, Array.Empty<string>(), platform, target, true);
				Assert.That(result.Success, Is.True, $"{platform} pass {p} {stage}: " + string.Join("\n", result.Messages.Select(m => m.message)));
				Assert.That(result.Messages.Any(m => m.severity == ShaderCompilerMessageSeverity.Error), Is.False);
				TestContext.WriteLine($"{platform} pass {p} {stage}: success={result.Success}, returned bytes={result.ShaderData.Length}");
			}
		}
	}
}
