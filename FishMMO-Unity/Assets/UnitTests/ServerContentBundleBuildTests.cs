using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Shared.CustomBuildTool.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Pipeline;
using UnityEditor.Rendering;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>Exercises native bundle writing/loading, including implicit visuals and cross-bundle physics.</summary>
	[TestFixture]
	public class ServerContentBundleBuildTests
	{
		[Test]
		public void SbpServerBundleOmitsShadersBeforeEnumeration_PreservesPhysics_ThenClientKeepsVisuals()
		{
			BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
			if (BuildPipeline.GetBuildTargetGroup(target) != BuildTargetGroup.Standalone)
				Assert.Ignore("Run this content-bundle integration test with a Standalone target selected.");
			string assets = "Assets/ServerContentBuildFixture_" + Guid.NewGuid().ToString("N");
			string output = Path.Combine(Path.GetTempPath(), "fishmmo-content-test-" + Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(assets);
			try
			{
				string shaderPath = assets + "/Fixture.shader";
				File.WriteAllText(shaderPath, "Shader \"Hidden/FishMMO/ServerContentBuildTest\" { SubShader { Pass { CGPROGRAM\n" +
					"#pragma vertex vert\n#pragma fragment frag\n#pragma multi_compile _ FIXTURE_ON\n" +
					"float4 vert(float4 p : POSITION) : SV_POSITION { return p; }\n" +
					"float4 frag() : SV_Target { return float4(1, 1, 1, 1); }\nENDCG\n} } }");
				AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);
				var material = new Material(AssetDatabase.LoadAssetAtPath<Shader>(shaderPath));
				string materialPath = assets + "/Fixture.mat";
				AssetDatabase.CreateAsset(material, materialPath);
				var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
				string meshPath = assets + "/Collision.asset";
				AssetDatabase.CreateAsset(mesh, meshPath);
				var terrain = new TerrainData { heightmapResolution = 33, size = new Vector3(32, 10, 32) };
				terrain.SetHeights(0, 0, new float[,] { { 0.5f } });
				string terrainPath = assets + "/Terrain.asset";
				AssetDatabase.CreateAsset(terrain, terrainPath);
				var root = new GameObject("server collision fixture");
				string prefabPath = assets + "/World.prefab";
				try
				{
					root.AddComponent<MeshFilter>().sharedMesh = mesh;
					root.AddComponent<MeshCollider>().sharedMesh = mesh;
					root.AddComponent<TerrainCollider>().terrainData = terrain;
					root.AddComponent<MeshRenderer>().sharedMaterial = material;
					PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
				}
				finally { UnityEngine.Object.DestroyImmediate(root); }
				var builds = new[] {
					new AssetBundleBuild { assetBundleName = "visual", assetNames = new[] { materialPath, shaderPath } },
					new AssetBundleBuild { assetBundleName = "world", assetNames = new[] { prefabPath } },
					new AssetBundleBuild { assetBundleName = "geometry", assetNames = new[] { meshPath, terrainPath } }
				};
				foreach (bool server in new[] { true, false })
				{
					ServerContentVariantObserver.Variants = 0;
					ServerContentVariantObserver.Calls = 0;
					string destination = Path.Combine(output, server ? "server" : "client");
					Directory.CreateDirectory(destination);
					using (new ServerContentBuildScope(server))
					{
						Assert.That(CompatibilityBuildPipeline.BuildAssetBundles(destination, builds,
							BuildAssetBundleOptions.None, target), Is.Not.Null);
					}
					if (server)
					{
						Assert.That(ServerContentVariantObserver.Calls, Is.Zero, "server shaders must never enter native variant preparation, even before preprocessing");
						Assert.That(ServerContentVariantObserver.Variants, Is.Zero, "no GPU variants should reach compilation in server bundles");
					}
					else
						Assert.That(ServerContentVariantObserver.Variants, Is.GreaterThan(0), "server stripping must not leak into a subsequent client build or its cache");
					Assert.That(material.shader, Is.Not.Null, "the source material must survive both builds");
					var loaded = new List<AssetBundle>();
					try
					{
						foreach (string bundleName in new[] { "geometry", "visual", "world" })
						{
							var bundle = AssetBundle.LoadFromFile(Path.Combine(destination, bundleName));
							Assert.That(bundle, Is.Not.Null);
							loaded.Add(bundle);
						}
						var prefab = loaded[2].LoadAsset<GameObject>(prefabPath);
						Assert.That(prefab, Is.Not.Null);
						Assert.That(prefab.GetComponent<MeshCollider>().sharedMesh.vertexCount, Is.EqualTo(3));
						Assert.That(prefab.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(prefab.GetComponent<MeshCollider>().sharedMesh));
						Assert.That(prefab.GetComponent<TerrainCollider>().terrainData.GetHeight(0, 0), Is.EqualTo(5).Within(0.01));
						if (server)
						{
							Assert.That(prefab.GetComponent<MeshRenderer>().sharedMaterial, Is.Null);
							Assert.That(loaded[1].LoadAllAssets<Shader>(), Is.Empty);
							Assert.That(loaded[1].LoadAllAssets<Material>(), Is.Empty);
						}
						else Assert.That(prefab.GetComponent<MeshRenderer>().sharedMaterial.shader, Is.Not.Null);
					}
					finally { foreach (var bundle in loaded) bundle.Unload(true); }
				}
			}
			finally
			{
				AssetDatabase.DeleteAsset(assets);
				if (Directory.Exists(output)) Directory.Delete(output, true);
			}
		}
	}

	public sealed class ServerContentVariantObserver : IPreprocessShaders
	{
		public static int Variants;
		public static int Calls;
		public int callbackOrder => int.MaxValue;
		public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
		{
			if (shader.name == "Hidden/FishMMO/ServerContentBuildTest") { Calls++; Variants += data.Count; }
		}
	}
}
