using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Shared.CustomBuildTool.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.UnitTests
{
	public class WebGLRenderPipelineScopeTests
	{
		private static List<RenderPipelineAsset> Pipelines()
		{
			var all = new HashSet<RenderPipelineAsset> { GraphicsSettings.defaultRenderPipeline };
			for (int i = 0; i < QualitySettings.names.Length; i++) all.Add(QualitySettings.GetRenderPipelineAssetAt(i));
			var result = new List<RenderPipelineAsset>();
			foreach (var asset in all)
			{
				if (asset == null) continue;
				using var serialized = new SerializedObject(asset);
				if (serialized.FindProperty("m_GPUResidentDrawerMode") != null) result.Add(asset);
			}
			return result;
		}

		private static int Mode(RenderPipelineAsset asset, int? value = null)
		{
			using var serialized = new SerializedObject(asset);
			var property = serialized.FindProperty("m_GPUResidentDrawerMode");
			int before = property.intValue;
			if (value.HasValue) { property.intValue = value.Value; serialized.ApplyModifiedPropertiesWithoutUndo(); }
			return before;
		}

		[Test]
		public void BrowserScopeRestoresEveryPipelineAndBrgSettingAfterNestedScopeAndFailure()
		{
			var assets = Pipelines();
			Assert.That(assets.Count, Is.GreaterThan(0));
			int previous = Mode(assets[0], 1);
			var before = assets.ConvertAll(asset => EditorJsonUtility.ToJson(asset));
			var brg = EditorGraphicsSettings.batchRendererGroupShaderStrippingMode;
			try
			{
				Assert.Throws<InvalidOperationException>(() =>
				{
					using var outer = new WebGLRenderPipelineScope(true);
					foreach (var asset in assets) Assert.That(Mode(asset), Is.Zero);
					Assert.That(EditorGraphicsSettings.batchRendererGroupShaderStrippingMode, Is.EqualTo(BatchRendererGroupStrippingMode.KeepAll));
					using (new WebGLRenderPipelineScope(true))
						foreach (var asset in assets) Assert.That(Mode(asset), Is.Zero);
					foreach (var asset in assets) Assert.That(Mode(asset), Is.Zero, "an inner scope must not restore native settings early");
					throw new InvalidOperationException("build failed");
				});
				for (int i = 0; i < assets.Count; i++) Assert.That(EditorJsonUtility.ToJson(assets[i]), Is.EqualTo(before[i]));
				Assert.That(EditorGraphicsSettings.batchRendererGroupShaderStrippingMode, Is.EqualTo(brg));
				Assert.That(File.Exists("Library/FishMMO/WebGLRenderPipelineRecovery.json"), Is.False);
			}
			finally { Mode(assets[0], previous); AssetDatabase.SaveAssets(); }
		}

		[Test]
		public void DisabledScopeLeavesNativePipelineConfigurationUntouched()
		{
			var assets = Pipelines();
			var before = assets.ConvertAll(asset => EditorJsonUtility.ToJson(asset));
			var brg = EditorGraphicsSettings.batchRendererGroupShaderStrippingMode;
			using (new WebGLRenderPipelineScope(false))
				for (int i = 0; i < assets.Count; i++) Assert.That(EditorJsonUtility.ToJson(assets[i]), Is.EqualTo(before[i]));
			Assert.That(EditorGraphicsSettings.batchRendererGroupShaderStrippingMode, Is.EqualTo(brg));
		}

		[TestCase(false)]
		[TestCase(true)]
		public void InterruptedBrowserBuildRestoresSavedDrawerSettings(bool startNextBuildImmediately)
		{
			const string path = "Library/FishMMO/WebGLRenderPipelineRecovery.json";
			if (File.Exists(path)) Assert.Ignore("An existing interrupted build must recover first.");
			var assets = Pipelines(); Assert.That(assets.Count, Is.GreaterThan(0));
			int previous = Mode(assets[0], 0);
			string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(assets[0]));
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllText(path, "{\"brg\":" + (int)EditorGraphicsSettings.batchRendererGroupShaderStrippingMode +
					",\"pipelines\":[{\"guid\":\"" + guid + "\",\"mode\":1}]}");
				if (startNextBuildImmediately)
				{
					using (new WebGLRenderPipelineScope(true)) Assert.That(Mode(assets[0]), Is.Zero);
				}
				else WebGLRenderPipelineScope.RecoverInterruptedBuild();
				Assert.That(Mode(assets[0]), Is.EqualTo(1));
				Assert.That(File.Exists(path), Is.False);
			}
			finally { Mode(assets[0], previous); AssetDatabase.SaveAssets(); if (File.Exists(path)) File.Delete(path); }
		}
	}
}
