using System;
using System.Reflection;
using FishMMO.Shared.CustomBuildTool.Addressables;
using FishMMO.Shared.CustomBuildTool.Core;
using NUnit.Framework;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	[TestFixture]
	public class ServerAddressablesTests
	{
		[Test]
		public void ServerScopeBypassesBrokenClientRendererAndRestoresAllPipelinesAfterFailure()
		{
			var originalDefault = GraphicsSettings.defaultRenderPipeline;
			int selected = QualitySettings.GetQualityLevel();
			var originalQuality = QualitySettings.renderPipeline;
			var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
			var feature = ScriptableObject.CreateInstance<ServerAddressablesFixtureFeature>();
			var pipeline = UniversalRenderPipelineAsset.Create(renderer);
			try
			{
				// This is the state that crashed URP's callback constructor in the real build.
				renderer.rendererFeatures.Add(feature);
				renderer.rendererFeatures.Add(null);
				GraphicsSettings.defaultRenderPipeline = pipeline;
				QualitySettings.renderPipeline = pipeline;
				var before = new RenderPipelineAsset[QualitySettings.names.Length];
				for (int index = 0; index < before.Length; index++) before[index] = QualitySettings.GetRenderPipelineAssetAt(index);

				Type preprocessor = Type.GetType("UnityEditor.Rendering.Universal.ShaderBuildPreprocessor, Unity.RenderPipelines.Universal.Editor", true);
				FieldInfo stripping = preprocessor.GetField("s_StripUnusedVariants", BindingFlags.Static | BindingFlags.Public);
				bool oldStripping = (bool)stripping.GetValue(null);
				try
				{
					stripping.SetValue(null, true);
					MethodInfo gather = preprocessor.GetMethod("GatherShaderFeatures", BindingFlags.Static | BindingFlags.NonPublic);
					var failure = Assert.Throws<TargetInvocationException>(() => gather.Invoke(null, new object[] { false }));
					Assert.That(failure.InnerException, Is.TypeOf<NullReferenceException>());
					Assert.Throws<InvalidOperationException>(() =>
					{
						using (new ServerContentBuildScope(true))
						{
							Assert.That(GraphicsSettings.defaultRenderPipeline, Is.Null);
							Assert.That(QualitySettings.GetQualityLevel(), Is.EqualTo(selected));
							for (int index = 0; index < before.Length; index++)
								Assert.That(QualitySettings.GetRenderPipelineAssetAt(index), Is.Null);
							Assert.DoesNotThrow(() => gather.Invoke(null, new object[] { false }));
							throw new InvalidOperationException("simulated failed content build");
						}
					});
					Assert.That(GraphicsSettings.defaultRenderPipeline, Is.SameAs(pipeline));
					Assert.That(QualitySettings.GetQualityLevel(), Is.EqualTo(selected));
					for (int index = 0; index < before.Length; index++)
						Assert.That(QualitySettings.GetRenderPipelineAssetAt(index), Is.SameAs(before[index]));
					Assert.That(renderer.rendererFeatures.Count, Is.EqualTo(2), "never edit the client's renderer asset to fix a server build");
				}
				finally { stripping.SetValue(null, oldStripping); }
			}
			finally
			{
				GraphicsSettings.defaultRenderPipeline = originalDefault;
				QualitySettings.SetQualityLevel(selected, false);
				QualitySettings.renderPipeline = originalQuality;
				renderer.rendererFeatures.Clear();
				Object.DestroyImmediate(feature);
				Object.DestroyImmediate(pipeline);
				Object.DestroyImmediate(renderer);
			}
		}

		[TestCase(null)]
		[TestCase("URP callback threw")]
		public void MissingOrFailedAddressablesResultStopsTheBuild(string error)
		{
			AddressablesPlayerBuildResult result = error == null ? null : new AddressablesPlayerBuildResult { Error = error };
			Assert.Throws<BuildFailedException>(() => AddressableManager.RequireSuccessfulContentBuild(result));
		}

		[Test]
		public void SuccessfulContentResultPassesTheBuildGate()
		{
			Assert.DoesNotThrow(() => AddressableManager.RequireSuccessfulContentBuild(new AddressablesPlayerBuildResult()));
		}

		[Test]
		public void FailedContentRestoresOriginalGroupFlagsAndPropagatesTheFailure()
		{
			var settings = AddressableAssetSettings.Create("", "FailureFixture", false, false);
			var group = settings.CreateGroup("Scene_Shared", true, false, false, null, typeof(BundledAssetGroupSchema));
			var schema = group.GetSchema<BundledAssetGroupSchema>();
			schema.IncludeInBuild = true;
			schema.UseAssetBundleCrc = false;
			schema.UseAssetBundleCrcForCachedBundles = true;
			schema.UseUnityWebRequestForLocalBundles = false;
			try
			{
				Assert.Throws<BuildFailedException>(() => new FailingManager(settings).BuildAddressablesWithExclusions(new[] { "Client" }));
				Assert.That(schema.IncludeInBuild, Is.True);
				Assert.That(schema.UseAssetBundleCrc, Is.False);
				Assert.That(schema.UseAssetBundleCrcForCachedBundles, Is.True);
				Assert.That(schema.UseUnityWebRequestForLocalBundles, Is.False);
			}
			finally
			{
				Object.DestroyImmediate(schema);
				Object.DestroyImmediate(group);
				Object.DestroyImmediate(settings);
			}
		}

		private sealed class FailingManager : AddressableManager
		{
			private readonly AddressableAssetSettings settings;
			public FailingManager(AddressableAssetSettings settings) { this.settings = settings; }
			protected override AddressableAssetSettings GetSettings() => settings;
			protected override void BuildContent(AddressableAssetSettings settings, string[] exclusions, bool crc, bool webRequest)
			{
				var schema = settings.DefaultGroup.GetSchema<BundledAssetGroupSchema>();
				schema.IncludeInBuild = false;
				schema.UseAssetBundleCrc = true;
				schema.UseAssetBundleCrcForCachedBundles = false;
				schema.UseUnityWebRequestForLocalBundles = true;
				RequireSuccessfulContentBuild(new AddressablesPlayerBuildResult { Error = "simulated Addressables failure" });
			}
		}
	}

	public sealed class ServerAddressablesFixtureFeature : ScriptableRendererFeature
	{
		public override void Create() { }
		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) { }
	}
}
