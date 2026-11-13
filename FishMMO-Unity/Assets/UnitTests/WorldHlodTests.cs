using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FishMMO.Client;
using FishMMO.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	public class WorldHlodTests
	{
		[Test]
		public void MainReplacementPreservesOriginalShadowLodAndFade()
		{
			var model = new FishModelData { Size = 10, SphereRadius = 5, LevelCount = 2, ShadowMask = 3, Transitions = new Vector4(.3f, .01f, 0, 0), FadeWidths = new Vector4(.06f, .002f, 0, 0) };
			var matrix = Matrix4x4.Translate(new Vector3(0, 0, 30)); var planes = new Plane[6];
			for (int i = 0; i < 6; i++) planes[i] = new Plane(Vector3.up, 1000);
			var normal = new List<TerrainGpuMath.Emit>(); var replaced = new List<TerrainGpuMath.Emit>();
			TerrainGpuMath.Cull(matrix, model, 1000, 1, Vector3.zero, planes, new Vector4(0,-1,0,100), new Vector3(1,0,0), normal);
			TerrainGpuMath.Cull(matrix, model, 1000, 1, Vector3.zero, planes, new Vector4(0,-1,0,100), new Vector3(1,0,0), replaced, false);
			Assert.That(normal.Any(e => e.View == 0), Is.True); Assert.That(replaced.Count, Is.GreaterThan(0));
			Assert.That(replaced.All(e => e.View == 1), Is.True);
			CollectionAssert.AreEqual(normal.Where(e => e.View == 1).Select(e => (e.Level,e.Fade)), replaced.Select(e => (e.Level,e.Fade)));
			Assert.That(Marshal.SizeOf<FishWork>(), Is.EqualTo(32));
		}
		[Test]
		public void SourceHashDetectsTransformAndPrototypeChanges()
		{
			var set = ScriptableObject.CreateInstance<ScenePropSet>();
			try
			{
				set.Prototypes = new ScenePropSet.Prototype[1]; set.Props = new[] { new ScenePropSet.Prop { Position = Vector3.one, Rotation = Quaternion.identity, Scale = Vector3.one } };
				uint original = WorldHlodData.SourceHash(set); var prop = set.Props[0]; prop.Position.x += .001f; set.Props[0] = prop;
				Assert.That(WorldHlodData.SourceHash(set), Is.Not.EqualTo(original)); prop.Position = Vector3.one; prop.Prototype = 1; set.Props[0] = prop;
				Assert.That(WorldHlodData.SourceHash(set), Is.Not.EqualTo(original));
			}
			finally { Object.DestroyImmediate(set); }
		}
		[Test]
		public void BakedGeometryPreservesWorldPositionsNormalsUvsAndPropOrigins()
		{
			var mesh = new Mesh();
			try
			{
				mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.forward }; mesh.normals = Enumerable.Repeat(Vector3.up,3).ToArray(); mesh.tangents = Enumerable.Repeat(new Vector4(1,0,0,1),3).ToArray(); mesh.uv = new[] { Vector2.zero,Vector2.right,Vector2.up }; mesh.triangles = new[] { 0,2,1 };
				var matrix = Matrix4x4.TRS(new Vector3(51,12,-37), Quaternion.Euler(15,62,7), new Vector3(2,3,4));
				var p = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<Vector4>(); var uv = new List<Vector2>(); var origins = new List<Vector4>(); var indices = new List<int>();
				FishMMO.TestHarness.World.Editor.WorldHlodBaker.AppendGeometry(matrix, mesh,0,new Vector4(2,3,.5f,.25f),7,p,n,t,uv,origins,indices);
				for (int i = 0; i < 3; i++) { Assert.That(Vector3.Distance(p[i],matrix.MultiplyPoint3x4(mesh.vertices[i])),Is.LessThan(1e-5)); Assert.That(Vector3.Distance(n[i],matrix.inverse.transpose.MultiplyVector(Vector3.up).normalized),Is.LessThan(1e-5)); Assert.That(origins[i],Is.EqualTo(new Vector4(7,51,12,-37))); }
				Assert.That(uv[1],Is.EqualTo(new Vector2(2.5f,.25f))); CollectionAssert.AreEqual(new[] {0,2,1},indices);
			}
			finally { Object.DestroyImmediate(mesh); }
		}
		[Test]
		public void InvalidOrStaleBakesFallBackInsteadOfReplacingSources()
		{
			var data = ScriptableObject.CreateInstance<WorldHlodData>(); var set = ScriptableObject.CreateInstance<ScenePropSet>(); var mesh = new Mesh { vertices = new[] { Vector3.zero,Vector3.right,Vector3.up }, triangles = new[] {0,1,2} }; var mat = new Material(Shader.Find("FishMMO/Weather Lit"));
			try
			{
				set.Props = new[] { new ScenePropSet.Prop { Rotation = Quaternion.identity,Scale = Vector3.one } };
				data.Sources = new[] { new WorldHlodData.Source { Set = set,Props = 1,TransformHash = WorldHlodData.SourceHash(set) } };
				data.Clusters = new[] { new WorldHlodData.Cluster { MinSourceSize = 1, Members = new[] {new WorldHlodData.Member { Source = 0,Prop = 0 }},Parts = new[] {new WorldHlodData.Part {Mesh = mesh,Material = mat}} } };
				Assert.That(data.SourcesMatch(),Is.True); data.Clusters[0].Members = new[] {new WorldHlodData.Member {Source = 0,Prop = 0},new WorldHlodData.Member {Source = 0,Prop = 0}}; Assert.That(data.SourcesMatch(),Is.False);
				data.Clusters[0].Members = new[] {new WorldHlodData.Member {Source = 0,Prop = 0}}; var prop = set.Props[0]; prop.Position = Vector3.right; set.Props[0] = prop; Assert.That(data.SourcesMatch(),Is.False);
			}
			finally { Object.DestroyImmediate(data); Object.DestroyImmediate(set); Object.DestroyImmediate(mesh); Object.DestroyImmediate(mat); }
		}

		[TestCase(ShaderCompilerPlatform.Vulkan, BuildTarget.StandaloneLinux64)]
		[TestCase(ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64)]
		[TestCase(ShaderCompilerPlatform.WebGPU, BuildTarget.WebGL)]
		public void HlodForwardAndDepthVariantsCompile(ShaderCompilerPlatform platform, BuildTarget target)
		{
			var shader = AssetDatabase.LoadAssetAtPath<Shader>(ProjectFiles.Path("Assets/Prefabs/Client/Weather/Shaders/FishWorldHlod.shader"));
			Assert.That(shader, Is.Not.Null);
			var sub = ShaderUtil.GetShaderData(shader).GetSubshader(0);
			for (int p = 0; p < 3; p++) foreach (var stage in new[] { ShaderType.Vertex, ShaderType.Fragment })
			{
				var result = sub.GetPass(p).CompileVariant(stage, new[] { "PROCEDURAL_INSTANCING_ON", "_CLUSTER_LIGHT_LOOP", "_NORMALMAP", "_MAIN_LIGHT_SHADOWS_CASCADE", "_SHADOWS_SOFT" }, platform, target);
				Assert.That(result.Success, Is.True, p + " " + stage + "\n" + string.Join("\n", result.Messages.Select(m => m.message)));
				Assert.That(result.Messages.Any(m => m.severity == ShaderCompilerMessageSeverity.Error), Is.False);
				// This API returns vertex data but no fragment bytecode on Vulkan/WebGPU. Also validate the real player/bundle builds.
				if (stage == ShaderType.Vertex || platform == ShaderCompilerPlatform.D3D) Assert.That(result.ShaderData.Length, Is.GreaterThan(0), platform + " pass " + p + " " + stage + ": " + string.Join("\n", result.Messages.Select(m => m.message)));
			}
		}
		[Test, Timeout(600000)]
		public void ReplacementAndProxyCullingCompileForWebGpu()
		{
			if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL,BuildTarget.WebGL)) Assert.Ignore("WebGL support required");
			string output = Path.Combine(Path.GetTempPath(),"fish-hlod-webgpu-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
			try
			{
				using var scope = new FishMMO.Shared.CustomBuildTool.Core.WebGLRenderPipelineScope(true);
				var result = UnityEditor.Build.Pipeline.CompatibilityBuildPipeline.BuildAssetBundles(output,new[] { new AssetBundleBuild { assetBundleName = "hlod-compute",assetNames = new[] {"Assets/Prefabs/Client/Weather/Shaders/FishWorldHlodCull.compute","Assets/Prefabs/Client/Weather/Shaders/FishTerrainInstancing.compute","Assets/TestHarness/World/Editor/TerrainIndexFilterProbe.compute","Assets/TestHarness/World/Editor/GrassLookCacheProbe.compute"} } },BuildAssetBundleOptions.ForceRebuildAssetBundle,BuildTarget.WebGL);
				Assert.That(result,Is.Not.Null); Assert.That(new FileInfo(Path.Combine(output,"hlod-compute")).Length,Is.GreaterThan(0));
			}
			finally { Directory.Delete(output,true); }
		}

		[Test]
		public void CostSelectionPrunesWasteWithoutDiscardingIndependentSavings()
		{
			var groups = new[] { new WorldHlodCostMath.Cluster(new[] {0},2),new WorldHlodCostMath.Cluster(new[] {1},10) }; var scratch = new WorldHlodCostMath.Scratch();
			int saving = WorldHlodCostMath.Select(new[] {1,2},new[] {1,1},new[] {10,5},groups,scratch,out int avoided,out int proxies);
			Assert.That(saving,Is.EqualTo(8)); Assert.That(avoided,Is.EqualTo(10)); Assert.That(proxies,Is.EqualTo(2)); Assert.That(scratch.Selected[0],Is.True); Assert.That(scratch.Selected[1],Is.False);
		}
		[Test]
		public void SharedModelsCountAsSavedOnlyWhenEveryVisibleInstanceIsReplaced()
		{
			var groups = new[] { new WorldHlodCostMath.Cluster(new[] {0},2),new WorldHlodCostMath.Cluster(new[] {0},2) }; var scratch = new WorldHlodCostMath.Scratch();
			Assert.That(WorldHlodCostMath.Select(new[] {3},new[] {2},new[] {20},groups,scratch,out _,out _),Is.Zero); Assert.That(scratch.Selected[0] || scratch.Selected[1],Is.False);
			Assert.That(WorldHlodCostMath.Select(new[] {2},new[] {2},new[] {20},groups,scratch,out _,out _),Is.EqualTo(16)); Assert.That(scratch.Selected[0] && scratch.Selected[1],Is.True);
		}

		[Test]
		public void InspectLocalBakedSceneCoverage()
		{
			string path = "Assets/Scenes/WorldScene/Moron/Moron 2 HLOD/WorldHlod 1.asset";
			var data = AssetDatabase.LoadAssetAtPath<WorldHlodData>(ProjectFiles.Path("Assets/Scenes/WorldScene/Moron/Moron 2 HLOD/WorldHlod.asset")) ?? AssetDatabase.LoadAssetAtPath<WorldHlodData>(ProjectFiles.Path(path)); if (data == null) Assert.Ignore("Local generated HLOD bake not present");
			var lines = new List<string> {"cluster,x,y,z,radius,min_distance_factor,max_distance_factor,min_size,entry_at_fov60_bias2,exit_at_fov60_bias2"};
			float factor = TerrainTreeMath.ScreenFactor(60,false,0,2); int usable = 0;
			foreach (var group in data.Clusters)
			{
				float low = Mathf.Max(20,group.MinDistanceFactor*factor), high = Mathf.Min(CliffRockInstancing.DrawDistanceFor(group.MinSourceSize)*.85f,group.MaxDistanceFactor*factor);
				if (high > low + group.Bounds.extents.magnitude*2 + 3) usable++;
				lines.Add(string.Join(",",new[] { (float)(lines.Count-1),group.Bounds.center.x,group.Bounds.center.y,group.Bounds.center.z,group.Bounds.extents.magnitude,group.MinDistanceFactor,group.MaxDistanceFactor,group.MinSourceSize,low,high }.Select(v=>v.ToString("R",System.Globalization.CultureInfo.InvariantCulture))));
			}
			Directory.CreateDirectory("Library/RenderingBenchmark"); File.WriteAllLines("Library/RenderingBenchmark/hlod-data-coverage.csv",lines);
			Assert.That(data.SourcesMatch(),Is.True);
			var materials = new HashSet<Material>(); foreach (var group in data.Clusters) foreach (var part in group.Parts) { materials.Add(part.Material); var uv = new List<Vector4>(); part.Mesh.GetUVs(3,uv); var table = part.Material.GetTexture(TerrainLookMath.TableName); Assert.That(table,Is.Not.Null); foreach (var v in uv) Assert.That(v.x,Is.InRange(0f,table.height-1f)); }
			Debug.Log($"[HLOD materials] {materials.Count} materials for {data.Clusters.Sum(c=>c.Parts.Length)} parts.");
			Debug.Log($"[HLOD coverage] {usable}/{data.Clusters.Length} clusters have a conservative usable native-High-Fidelity window.");
		}

		[TestCase(false)]
		[TestCase(true)]
		public void CpuFlipbookEncodingPreservesEveryRgbaByte(bool linear)
		{
			var pixels = Enumerable.Range(0,256).Select(i => new Color32((byte)i,(byte)(255-i),(byte)(i*17),(byte)(i*11))).ToArray();
			var old = new Texture2D(16,16,TextureFormat.RGBA32,false,linear); var oldDecoded = new Texture2D(2,2,TextureFormat.RGBA32,false,true); var decoded = new Texture2D(2,2,TextureFormat.RGBA32,false,true);
			try
			{
				old.SetPixels32(pixels); old.Apply(false,false); oldDecoded.LoadImage(old.EncodeToPNG(),false);
				decoded.LoadImage(FishMMO.Shared.WorldDesign.TerrainArrayBaker.EncodeFlipbook(pixels,16,16),false);
				CollectionAssert.AreEqual(oldDecoded.GetPixels32(),decoded.GetPixels32()); CollectionAssert.AreEqual(pixels,decoded.GetPixels32());
			}
			finally { Object.DestroyImmediate(old); Object.DestroyImmediate(oldDecoded); Object.DestroyImmediate(decoded); }
		}

		[Test]
		public void GpuMainReplacementRetainsTheExactShadowOutput()
		{
			if (!SystemInfo.supportsComputeShaders) Assert.Ignore("GPU required");
			var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ProjectFiles.Path("Assets/Prefabs/Client/Weather/Shaders/FishTerrainInstancing.compute")));
			try
			{
				using var instances = new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,FishInstance.Stride); instances.SetData(new[] { FishInstance.From(Matrix4x4.Translate(new Vector3(0,0,30))) });
				using var models = new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,FishModelData.Stride); models.SetData(new[] { new FishModelData { Size = 1,SphereRadius = 1,LevelCount = 1,ShadowMask = 1 } });
				using var work = new GraphicsBuffer(GraphicsBuffer.Target.Structured,1,FishWork.Stride);
				using var slots = new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,8); slots.SetData(new[] { new Vector2Int(0,1), new Vector2Int(1,1) });
				using var counts = new GraphicsBuffer(GraphicsBuffer.Target.Raw,2,4);
				using var output = new GraphicsBuffer(GraphicsBuffer.Target.Structured,2,FishVisible.Stride);
				using var packed = new GraphicsBuffer(GraphicsBuffer.Target.Raw,1,4);
				int clear = shader.FindKernel("FishClearCounts"), cull = shader.FindKernel("FishCull");
				shader.SetInt("_FishSlotCount",2); shader.SetInt("_FishWorkCount",1); shader.SetInt("_FishPackedOn",0);
				shader.SetVector("_FishCameraPosition",Vector4.zero); shader.SetVector("_FishLight",new Vector4(0,-1,0,100)); shader.SetVector("_FishLod",new Vector4(1,0,0,0));
				shader.SetVectorArray("_FishPlanes",Enumerable.Repeat(new Vector4(0,1,0,1000),6).ToArray()); shader.SetVector("_FishHiZParams",Vector4.zero); shader.SetTexture(cull,"_FishHiZ",Texture2D.whiteTexture);
				shader.SetBuffer(clear,"_FishCounts",counts); shader.SetBuffer(cull,"_FishCounts",counts); shader.SetBuffer(cull,"_FishInstances",instances); shader.SetBuffer(cull,"_FishModels",models); shader.SetBuffer(cull,"_FishWork",work); shader.SetBuffer(cull,"_FishSlots",slots); shader.SetBuffer(cull,"_FishVisibleOut",output); shader.SetBuffer(cull,"_FishVisiblePacked",packed);
				var result = new uint[2]; var normal = new FishVisible[2]; var replaced = new FishVisible[2];
				foreach (uint flag in new uint[] {0,1})
				{
					work.SetData(new[] { new FishWork { Count = 1,Padding = flag,DrawDistance = 1000,LodBiasMultiplier = 1,ThinKeep = 1 } });
					shader.Dispatch(clear,1,1,1); shader.Dispatch(cull,1,1,1); counts.GetData(result);
					Assert.That(result[0],Is.EqualTo(flag == 0 ? 1u : 0u)); Assert.That(result[1],Is.EqualTo(1u));
					output.GetData(flag == 0 ? normal : replaced);
				}
				Assert.That(replaced[1].Index,Is.EqualTo(normal[1].Index)); Assert.That(replaced[1].LodFade,Is.EqualTo(normal[1].LodFade));
			}
			finally { Object.DestroyImmediate(shader); }
		}

		[StructLayout(LayoutKind.Sequential)] private struct Part { public Vector4 Sphere; public uint Cluster, P0, P1, P2; }
		[Test]
		public void GpuCullingKeepsCommandGeometryAndOnlyWritesInstanceCount()
		{
			if (!SystemInfo.supportsComputeShaders) Assert.Ignore("GPU required");
			var shader = Object.Instantiate(AssetDatabase.LoadAssetAtPath<ComputeShader>(ProjectFiles.Path("Assets/Prefabs/Client/Weather/Shaders/FishWorldHlodCull.compute")));
			try
			{
				using var parts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, 32);
				parts.SetData(new[] { new Part { Sphere = new Vector4(0,0,0,1), Cluster = 0 }, new Part { Sphere = new Vector4(0,0,0,1), Cluster = 1 }, new Part { Sphere = new Vector4(-20,0,0,1), Cluster = 0 } });
				using var selected = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2, 4); selected.SetData(new uint[] { 1, 0 });
				using var args = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.IndirectArguments, 15, 4);
				var words = new uint[] { 3,99,6,12,0, 9,99,7,13,0, 12,99,8,14,0 }; args.SetData(words);
				int kernel = shader.FindKernel("CullHlod");
				shader.SetBuffer(kernel,"_HlodParts",parts); shader.SetBuffer(kernel,"_HlodSelected",selected); shader.SetBuffer(kernel,"_HlodArgs",args); shader.SetInt("_HlodPartCount",3);
				shader.SetVectorArray("_HlodPlanes", Enumerable.Repeat(new Vector4(1,0,0,10),6).ToArray());
				shader.SetVector("_FishHiZParams",Vector4.zero); shader.SetTexture(kernel,"_FishHiZ",Texture2D.whiteTexture);
				shader.Dispatch(kernel,1,1,1); var result = new uint[15]; args.GetData(result);
				for (int i = 0; i < 15; i++) Assert.That(result[i], Is.EqualTo(i % 5 == 1 ? (i == 1 ? 1u : 0u) : words[i]), "word " + i);
			}
			finally { Object.DestroyImmediate(shader); }
		}
	}
}
