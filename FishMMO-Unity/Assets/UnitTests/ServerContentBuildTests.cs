using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Shared.CustomBuildTool.Addressables;
using FishMMO.Shared.CustomBuildTool.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build.Pipeline;
using UnityEditor.Build.Pipeline.Interfaces;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	[TestFixture]
	public class ServerContentBuildTests
	{
		[Test]
		public void ServerShadersAreStrippedWhilePlayerSubtargetIsActive_ThenClientVariantsSurvive()
		{
			var processor = new ServerContentBuildProcessor();
			var variants = new List<ShaderCompilerData> { default, default };
			StandaloneBuildSubtarget previous = EditorUserBuildSettings.standaloneBuildSubtarget;
			try
			{
				EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
				using (new ServerContentBuildScope(true))
				{
					processor.OnProcessShader(null, default, variants);
					Assert.That(variants, Is.Empty);
					variants.Add(default);
					processor.OnProcessComputeShader(null, "Kernel", variants);
					Assert.That(variants, Is.Empty);
				}
				variants.Add(default);
				using (new ServerContentBuildScope(false))
				{
					processor.OnProcessShader(null, default, variants);
					processor.OnProcessComputeShader(null, "Kernel", variants);
				}
				Assert.That(variants.Count, Is.EqualTo(1), "a client build following a server build must retain variants");
			}
			finally { EditorUserBuildSettings.standaloneBuildSubtarget = previous; }
		}

		[Test]
		public void FailedAndNestedBuildsRestoreScopeAndExistingSbpCallback()
		{
			BuildCallbacks callbacks = ContentPipeline.BuildCallbacks;
			var original = callbacks.PostScriptsCallbacks;
			int called = 0;
			Func<IBuildParameters, IBuildResults, ReturnCode> existing = (_, __) => { called++; return ReturnCode.Success; };
			callbacks.PostScriptsCallbacks = existing;
			try
			{
				Assert.Throws<InvalidOperationException>(() =>
				{
					using (new ServerContentBuildScope(true))
					{
						var server = Parameters();
						Assert.That(callbacks.PostScripts(server, null), Is.EqualTo(ReturnCode.Success));
						Assert.That(server.UseCache, Is.False, "server content must neither read nor write the client SBP cache");
						using (new ServerContentBuildScope(false))
						{
							var client = Parameters();
							callbacks.PostScripts(client, null);
							Assert.That(client.UseCache, Is.True);
							Assert.That(ServerContentBuildScope.IsActive, Is.False);
						}
						Assert.That(ServerContentBuildScope.IsActive, Is.True);
						throw new InvalidOperationException("build failure");
					}
				});
				Assert.That(ServerContentBuildScope.IsActive, Is.False);
				Assert.That(callbacks.PostScriptsCallbacks, Is.SameAs(existing));
				Assert.That(called, Is.EqualTo(2), "preserve other packages' callbacks");
				var nextClient = Parameters();
				callbacks.PostScripts(nextClient, null);
				Assert.That(nextClient.UseCache, Is.True);
			}
			finally { callbacks.PostScriptsCallbacks = original; }
		}

		private static BundleBuildParameters Parameters() => new BundleBuildParameters(
			BuildTarget.StandaloneLinux64, BuildTargetGroup.Standalone, Path.GetTempPath()) { UseCache = true };

		[TestCase(typeof(Shader), true)]
		[TestCase(typeof(ComputeShader), true)]
		[TestCase(typeof(Material), true)]
		[TestCase(typeof(ShaderVariantCollection), true)]
		[TestCase(typeof(Texture2D), false)]
		[TestCase(typeof(TerrainLayer), false)]
		[TestCase(typeof(TerrainData), false)]
		[TestCase(typeof(Mesh), false)]
		public void PackedContentStripsGpuProgramsButKeepsPotentialCpuGameplayData(Type type, bool stripped)
		{
			Assert.That(ServerContentPacking.IsGpuProgramOrMaterial(type), Is.EqualTo(stripped));
		}

		[TestCase("Client_Static_Permanent", false)]
		[TestCase("Scene_Client", false)]
		[TestCase("client_Dynamic", false)]
		[TestCase("Server_Static_Permanent", true)]
		[TestCase("Scene_Shared", true)]
		[TestCase("Generated_Biome_Art", true)]
		public void GeneratedDependencyRootsFollowTheSameGroupExclusions(string name, bool included)
		{
			Assert.That(ContentBuildFilter.IncludesGroup(name, new[] { "Client" }), Is.EqualTo(included));
		}

		[TestCase(typeof(Material), true)]
		[TestCase(typeof(Texture2D), true)]
		[TestCase(typeof(Shader), true)]
		[TestCase(typeof(ComputeShader), true)]
		[TestCase(typeof(ShaderVariantCollection), true)]
		[TestCase(typeof(TerrainLayer), true)]
		[TestCase(typeof(Mesh), false)]
		[TestCase(typeof(TerrainData), false)]
		[TestCase(typeof(GameObject), false)]
		[TestCase(typeof(TextAsset), false)]
		public void RenderingRootFilterPreservesGeometryTerrainPrefabsAndData(Type type, bool renderingOnly)
		{
			Assert.That(ContentBuildFilter.IsRenderingOnly(type), Is.EqualTo(renderingOnly));
		}

		[Test]
		public void ServerSceneRetainsCollisionGeometryAndTerrain_AndDoesNotModifySharedMaterials()
		{
			Scene scene = EditorSceneManager.NewPreviewScene();
			var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
			var data = new TerrainData { heightmapResolution = 33, size = new Vector3(32, 10, 32) };
			var material = new Material(Shader.Find("Hidden/InternalErrorShader"));
			try
			{
				var root = new GameObject("server world");
				SceneManager.MoveGameObjectToScene(root, scene);
				root.AddComponent<MeshFilter>().sharedMesh = mesh;
				var renderer = root.AddComponent<MeshRenderer>();
				renderer.sharedMaterials = new[] { material };
				var collider = root.AddComponent<MeshCollider>();
				collider.sharedMesh = mesh;
				var terrainObject = Terrain.CreateTerrainGameObject(data);
				SceneManager.MoveGameObjectToScene(terrainObject, scene);
				var terrain = terrainObject.GetComponent<Terrain>();
				terrain.materialTemplate = material;
				var processor = new ServerContentBuildProcessor();
				using (new ServerContentBuildScope(false)) processor.OnProcessScene(scene, null);
				Assert.That(renderer.sharedMaterial, Is.SameAs(material), "client scenes must retain their rendering");
				using (new ServerContentBuildScope(true)) processor.OnProcessScene(scene, null);
				Assert.That(renderer.sharedMaterials, Is.Empty);
				Assert.That(terrain.materialTemplate, Is.Null);
				Assert.That(root.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
				Assert.That(collider.sharedMesh, Is.SameAs(mesh));
				Assert.That(terrain.terrainData, Is.SameAs(data));
				Assert.That(terrainObject.GetComponent<TerrainCollider>().terrainData, Is.SameAs(data));
				Assert.That(data.heightmapResolution, Is.EqualTo(33));
				Assert.That(material.shader, Is.Not.Null, "clearing a scene reference must not edit the shared material");
			}
			finally
			{
				EditorSceneManager.ClosePreviewScene(scene);
				Object.DestroyImmediate(material);
				Object.DestroyImmediate(data);
				Object.DestroyImmediate(mesh);
			}
		}
	}
}
