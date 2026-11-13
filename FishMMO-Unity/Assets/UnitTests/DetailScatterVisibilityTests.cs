using System;
using System.IO;
using FishMMO.Client;
using FishMMO.Shared.CustomBuildTool.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Pipeline;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	public class DetailScatterVisibilityTests
	{
		private const string ShaderPath = "Assets/Prefabs/Client/Weather/Shaders/FishDetailScatter.compute";
		private struct Region { public uint Start, Capacity; }
		private struct Model { public Vector4 Sphere, Extra, Lod; }
		private struct Visible { public uint Index; public float Fade; }

		[TestCase(true, false)]
		[TestCase(false, false)]
		[TestCase(true, true)]
		public void VisibilityRefreshRetainsSourcesAndShadowsAndRestoresRevealedDetails(bool depth, bool overflowing)
		{
			if (!SystemInfo.supportsComputeShaders || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
				Assert.Ignore("Requires a real GPU");
			var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ProjectFiles.Path(ShaderPath)));
			var texture = new Texture2D(8, 8, TextureFormat.RFloat, false, true);
			try
			{
				var pixels = new Color[64]; for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(5, 0, 0, 0);
				texture.SetPixels(pixels); texture.Apply();
				using var models = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, 48);
				models.SetData(new[] { new Model { Sphere = new Vector4(0, 0, 0, .1f) } });
				using var slots = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 6, 8);
				slots.SetData(new[] { new Region { Start = 0, Capacity = 3 }, new Region { Start = 4, Capacity = 2 }, new Region(), new Region(), new Region(), new Region() });
				using var counts = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 6, 4);
				uint[] sourceCounts = { overflowing ? 50u : 3u, 2u, 0u, 0u, 0u, 0u }; counts.SetData(sourceCounts);
				using var instances = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, FishInstance.Stride);
				var sourceInstances = new FishInstance[8];
				sourceInstances[0] = FishInstance.From(Matrix4x4.Translate(new Vector3(0, 0, 3)));
				sourceInstances[1] = FishInstance.From(Matrix4x4.Translate(new Vector3(0, 0, 10)));
				sourceInstances[2] = FishInstance.From(Matrix4x4.Translate(new Vector3(30, 0, 10)));
				instances.SetData(sourceInstances);
				using var visible = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, 8);
				var sourceVisible = new Visible[8];
				for (uint i = 0; i < 8; i++) sourceVisible[i] = new Visible { Index = i, Fade = .25f + i * .05f };
				visible.SetData(sourceVisible);
				using var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, 8);
				using var outputCounts = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 6, 4);
				// The culled list packed as the batch group reads it (DetailScatterRenderer.Brg.cs): resident index | fade byte.
				using var packed = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 8, 4);
				int clear = shader.FindKernel("FishScatterClear"), cull = shader.FindKernel("FishScatterCull");
				shader.SetInt("_ScatterSlotCount", 6);
				var view = Matrix4x4.Scale(new Vector3(1, 1, -1));
				var projection = Matrix4x4.Perspective(60, 1, .1f, 100);
				var planes = GeometryUtility.CalculateFrustumPlanes(projection * view);
				var vectors = new Vector4[6];
				for (int i = 0; i < 6; i++) vectors[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);
				shader.SetVectorArray("_ScatterPlanes", vectors);
				shader.SetMatrix("_FishHiZViewProj", GL.GetGPUProjectionMatrix(projection, true) * view);
				shader.SetVector("_FishHiZEye", Vector4.zero);
				shader.SetVector("_FishHiZForward", new Vector4(0, 0, 1, SystemInfo.graphicsUVStartsAtTop ? 1 : 0));
				shader.SetTexture(cull, "_FishHiZ", texture);
				shader.SetBuffer(cull, "_ScatterModels", models); shader.SetBuffer(cull, "_ScatterSlots", slots);
				shader.SetBuffer(cull, "_ScatterCounts", counts); shader.SetBuffer(cull, "_ScatterInstancesOut", instances);
				shader.SetBuffer(cull, "_ScatterVisibleOut", visible); shader.SetBuffer(cull, "_ScatterCulledVisible", output);
				shader.SetBuffer(cull, "_ScatterCulledCounts", outputCounts); shader.SetBuffer(clear, "_ScatterCounts", outputCounts);
				shader.SetBuffer(cull, "_ScatterCulledPacked", packed); shader.SetInt("_ScatterDotsOn", 1);
				void Refresh(bool useDepth)
				{
					shader.SetVector("_FishHiZParams", new Vector4(8, 8, 1, useDepth ? 1 : 0));
					shader.Dispatch(clear, 1, 1, 1); shader.Dispatch(cull, 6, 1, 1);
				}
				var actualCounts = new uint[6]; var actual = new Visible[8];
				Refresh(depth); outputCounts.GetData(actualCounts); output.GetData(actual);
				Assert.That(actualCounts[0], Is.EqualTo(depth ? 1 : 2));
				Assert.That(actualCounts[1], Is.EqualTo(2), "camera occlusion never clips the source shadow list");
				Assert.That(actual[0].Index, Is.InRange(0u, 1u));
				Assert.That(actual[0].Fade, Is.EqualTo(sourceVisible[actual[0].Index].Fade), "LOD crossfade preserved");
				var actualPacked = new uint[8]; packed.GetData(actualPacked);
				for (int i = 0; i < actualCounts[0]; i++)
					Assert.That(actualPacked[i], Is.EqualTo(TerrainBrgMath.PackVisible(actual[i].Index, actual[i].Fade)), $"packed entry {i} is the culled entry as DOTS reads it");
				// A cached source can reappear without any regeneration of positions or density.
				Refresh(false); outputCounts.GetData(actualCounts); Assert.That(actualCounts[0], Is.EqualTo(2));
				sourceInstances[1] = FishInstance.From(Matrix4x4.Translate(new Vector3(0, 0, 4)));
				instances.SetData(sourceInstances); Refresh(true); outputCounts.GetData(actualCounts);
				Assert.That(actualCounts[0], Is.EqualTo(2));
				var unchanged = new uint[6]; counts.GetData(unchanged); CollectionAssert.AreEqual(sourceCounts, unchanged);
				var unchangedVisible = new Visible[8]; visible.GetData(unchangedVisible); CollectionAssert.AreEqual(sourceVisible, unchangedVisible);
			}
			finally { Object.DestroyImmediate(shader); Object.DestroyImmediate(texture); }
		}

		[Test]
		public void ThePack_WritesEachSlotsGeneratedList_AsTheBatchGroupReadsIt()
		{
			if (!SystemInfo.supportsComputeShaders || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
				Assert.Ignore("Requires a real GPU");
			var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ProjectFiles.Path(ShaderPath)));
			try
			{
				using var slots = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, 8);
				slots.SetData(new[] { new Region { Start = 0, Capacity = 3 }, new Region { Start = 5, Capacity = 2 } });
				using var counts = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 2, 4);
				// Slot 1 overflowed: only its capacity is packed.
				counts.SetData(new uint[] { 3u, 9u });
				using var visible = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 8, 8);
				var source = new Visible[8];
				for (uint i = 0; i < 8; i++) source[i] = new Visible { Index = 100 + i, Fade = i % 3 == 0 ? 0f : (i % 2 == 0 ? -0.4f : 0.7f) };
				visible.SetData(source);
				using var packed = new GraphicsBuffer(GraphicsBuffer.Target.Raw, 8, 4);
				var untouched = new uint[8]; for (int i = 0; i < 8; i++) untouched[i] = 0xDEADBEEFu;
				packed.SetData(untouched);
				int pack = shader.FindKernel("FishScatterPack");
				shader.SetInt("_ScatterSlotCount", 2);
				shader.SetBuffer(pack, "_ScatterSlots", slots); shader.SetBuffer(pack, "_ScatterCounts", counts);
				shader.SetBuffer(pack, "_ScatterVisibleOut", visible); shader.SetBuffer(pack, "_ScatterPackedOut", packed);
				shader.Dispatch(pack, 2, 1, 1);
				var actual = new uint[8]; packed.GetData(actual);
				foreach (int i in new[] { 0, 1, 2, 5, 6 })
					Assert.That(actual[i], Is.EqualTo(TerrainBrgMath.PackVisible(source[i].Index, source[i].Fade)), $"entry {i}");
				foreach (int i in new[] { 3, 4, 7 })
					Assert.That(actual[i], Is.EqualTo(0xDEADBEEFu), $"entry {i} lies outside every slot's filled run");
			}
			finally { Object.DestroyImmediate(shader); }
		}

		[Test, Timeout(600000)]
		public void ScatterComputeCompilesForWebGpu()
		{
			if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL)) Assert.Ignore("WebGL support required");
			CollectionAssert.AreEqual(new[] { GraphicsDeviceType.WebGPU }, PlayerSettings.GetGraphicsAPIs(BuildTarget.WebGL));
			string directory = Path.Combine(Path.GetTempPath(), "fish-scatter-webgpu-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(directory);
			try
			{
				using var scope = new WebGLRenderPipelineScope(true);
				var manifest = CompatibilityBuildPipeline.BuildAssetBundles(directory, new[] {
					new AssetBundleBuild { assetBundleName = "scatter-compute", assetNames = new[] { ShaderPath } }
				}, BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.WebGL);
				Assert.That(manifest, Is.Not.Null);
				Assert.That(new FileInfo(Path.Combine(directory, "scatter-compute")).Length, Is.GreaterThan(0));
			}
			finally { Directory.Delete(directory, true); }
		}
	}
}
