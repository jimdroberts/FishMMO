using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor.Build.Pipeline;
using FishMMO.Shared.CustomBuildTool.Core;
using System.Reflection;
using FishMMO.Client;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	public class TerrainDirectTextureTests
	{
		[Test]
		public void GpuBuildReadsEachTilesOwnHeightmapAndPreservesHoles()
		{
			if (!SystemInfo.supportsComputeShaders) Assert.Ignore("Requires a GPU");
			Scene scene = EditorSceneManager.NewPreviewScene();
			var data = new List<TerrainData>();
			var tiles = new List<Terrain>();
			var material = new Material(Shader.Find(TerrainArrayBinder.ShaderName));
			var compute = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(TerrainHeightmapSystem.ComputeAssetPath));
			Mesh mesh = (Mesh)typeof(TerrainHeightmapSystem).GetMethod("PatchMesh", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
			object field = null;
			Type type = typeof(TerrainHeightmapSystem).Assembly.GetType("FishMMO.Client.TerrainHeightmapField");
			try
			{
				for (int i = 0; i < 2; i++)
				{
					var d = new TerrainData { heightmapResolution = 33, size = new Vector3(32, 100, 32) }; data.Add(d);
					var heights = new float[33, 33]; for (int z = 0; z < 33; z++) for (int x = 0; x < 33; x++) heights[z, x] = i == 0 ? 0.2f : 0.7f;
					d.SetHeights(0, 0, heights);
					var holes = new bool[32, 32]; for (int z = 0; z < 32; z++) for (int x = 0; x < 32; x++) holes[z, x] = !(i == 1 && x > 4 && x < 10 && z > 4 && z < 10);
					d.SetHoles(0, 0, holes);
					GameObject go = Terrain.CreateTerrainGameObject(d); SceneManager.MoveGameObjectToScene(go, scene); go.transform.position = new Vector3(i * 32, 0, 0);
					Terrain tile = go.GetComponent<Terrain>(); tile.drawInstanced = true; tile.materialTemplate = material; tiles.Add(tile);
				}
				field = Activator.CreateInstance(type, new object[] { scene, tiles });
				bool built = (bool)type.GetMethod("Build").Invoke(field, new object[] { compute, AssetDatabase.LoadAssetAtPath<Shader>(TerrainHeightmapSystem.ShaderAssetPath), mesh });
				Assert.That(built, Is.True, (string)type.GetProperty("Refusal").GetValue(field));
				var nodes = (GraphicsBuffer)type.GetField("nodeBuffer", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(field);
				var values = new Vector4[nodes.count]; nodes.GetData(values);
				int n = TerrainHeightmapMath.NodesPerTile(32);
				Assert.That(values[0].x, Is.EqualTo(20).Within(0.02));
				Assert.That(values[n].x, Is.EqualTo(70).Within(0.02));
				Assert.That(values[0].w, Is.Zero);
				Assert.That(values[n].w, Is.EqualTo(1));
				var blocks = (MaterialPropertyBlock[])type.GetField("tileBlocks", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(field);
				for (int i = 0; i < 2; i++)
				{
					Assert.That(blocks[i].GetTexture("_FishTerrainHeights"), Is.EqualTo(data[i].heightmapTexture));
					Assert.That(blocks[i].GetTexture("_FishTerrainNormals"), Is.EqualTo(tiles[i].normalmapTexture));
					Assert.That(blocks[i].GetTexture("_FishHolesArray"), Is.EqualTo(data[i].holesTexture));
				}
				// Selection must put every patch in its own tile's command range.
				GraphicsBuffer Buffer(string name) => (GraphicsBuffer)type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(field);
				int clear = compute.FindKernel("FishTerrainClear"), select = compute.FindKernel("FishTerrainSelect");
				compute.SetBuffer(clear, "_FishTerrainArgs", Buffer("args")); compute.Dispatch(clear, 2, 1, 1);
				compute.SetVector("_FishTerrainLodView", new Vector4(16, 50, 16, 4));
				compute.SetVectorArray("_FishTerrainPlanes", new Vector4[6]); compute.SetVectorArray("_FishTerrainShadowPlanes", new Vector4[6]);
				compute.SetVector("_FishTerrainShadowSweep", new Vector4(0, 0, 0, 1));
				compute.SetBuffer(select, "_FishTerrainTiles", Buffer("tileBuffer"));
				compute.SetBuffer(select, "_FishTerrainNodes", nodes);
				compute.SetBuffer(select, "_FishTerrainTileShown", Buffer("shownBuffer"));
				compute.SetBuffer(select, "_FishTerrainArgs", Buffer("args"));
				compute.SetBuffer(select, "_FishTerrainPatchesOut", Buffer("patches"));
				compute.Dispatch(select, 1, 1, 1);
				var args = new uint[8 * 5]; Buffer("args").GetData(args);
				var selected = new uint[Buffer("patches").count * 2]; Buffer("patches").GetData(selected);
				int capacity = (int)type.GetField("capacity", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(field);
				for (int tile = 0; tile < 2; tile++)
				{
					Assert.That(args[(tile * 4) * 5 + 1] + args[(tile * 4 + 1) * 5 + 1], Is.GreaterThan(0));
					for (int list = 0; list < 4; list++)
					{
						int command = tile * 4 + list;
						uint count = args[command * 5 + 1]; Assert.That(count, Is.LessThanOrEqualTo(capacity));
						for (int slot = 0; slot < count; slot++) Assert.That(selected[(command * capacity + slot) * 2] & 0xffffu, Is.EqualTo(tile));
					}
				}

			}
			finally
			{
				if (field != null)
				{
					// The renderer runs only in play mode; this GPU fixture owns its edit-mode material cleanup.
					foreach (string name in new[] { "solid", "holed" })
					{
						FieldInfo property = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
						Object.DestroyImmediate((Material)property.GetValue(field)); property.SetValue(field, null);
					}
					type.GetMethod("Release").Invoke(field, null);
				}
				EditorSceneManager.ClosePreviewScene(scene);
				foreach (TerrainData d in data) Object.DestroyImmediate(d);
				Object.DestroyImmediate(material); Object.DestroyImmediate(compute); Object.DestroyImmediate(mesh);
			}
		}

		[Test, Timeout(600000)]
		public void TerrainComputeBundleBuildsForWebGpu()
		{
			if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL)) Assert.Ignore("WebGL support required");
			CollectionAssert.AreEqual(new[] { GraphicsDeviceType.WebGPU }, PlayerSettings.GetGraphicsAPIs(BuildTarget.WebGL));
			string output = Path.Combine(Path.GetTempPath(), "fish-terrain-webgpu-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(output);
			try
			{
				using var scope = new WebGLRenderPipelineScope(true);
				var manifest = CompatibilityBuildPipeline.BuildAssetBundles(output, new[] {
					new AssetBundleBuild { assetBundleName = "terrain-compute", assetNames = new[] { TerrainHeightmapSystem.ComputeAssetPath } }
				}, BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.WebGL);
				Assert.That(manifest, Is.Not.Null);
				Assert.That(new FileInfo(Path.Combine(output, "terrain-compute")).Length, Is.GreaterThan(0));
			}
			finally { Directory.Delete(output, true); }
		}

		[TestCase("FishMMO/Weather Lit Indirect", ShaderType.Vertex)]
		[TestCase("FishMMO/Weather Lit Indirect", ShaderType.Fragment)]
		[TestCase("FishMMO/Vegetation Indirect", ShaderType.Vertex)]
		[TestCase("FishMMO/Vegetation Indirect", ShaderType.Fragment)]
		public void BatchRendererGroupPassCompilesForWebGpu(string name, ShaderType stage)
		{
			if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL)) Assert.Ignore("WebGL support required");
			var shader = Shader.Find(name);
			var pass = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(0);
			var result = pass.CompileVariant(stage, new[] { "DOTS_INSTANCING_ON", "_CLUSTER_LIGHT_LOOP" }, ShaderCompilerPlatform.WebGPU, BuildTarget.WebGL);
			Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Messages.Select(m => m.message)));
		}

		[TestCase(0, ShaderType.Vertex)]
		[TestCase(0, ShaderType.Fragment)]
		[TestCase(1, ShaderType.Vertex)]
		[TestCase(1, ShaderType.Fragment)]
		[TestCase(3, ShaderType.Vertex)]
		[TestCase(4, ShaderType.Vertex)]
		[TestCase(4, ShaderType.Fragment)]
		public void DirectTilePassCompilesForWebGpu(int index, ShaderType stage)
		{
			if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL)) Assert.Ignore("WebGL support required");
			var shader = AssetDatabase.LoadAssetAtPath<Shader>(TerrainHeightmapSystem.ShaderAssetPath);
			var pass = ShaderUtil.GetShaderData(shader).GetSubshader(0).GetPass(index);
			var result = pass.CompileVariant(stage, new[] { "_ALPHATEST_ON", "_CLUSTER_LIGHT_LOOP" }, ShaderCompilerPlatform.WebGPU, BuildTarget.WebGL);
			Assert.That(result.Success, Is.True, string.Join(Environment.NewLine, result.Messages.Select(m => m.message)));
		}
	}
}
