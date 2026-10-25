#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Shared.CustomBuildTool.Core
{
	/// <summary>
	/// WebGPU cannot use Unity's GPU Resident Drawer. Serialize browser pipelines without that mode,
	/// which otherwise forces DOTS/probe-atlas shader paths. Restore the Editor/native configuration
	/// after content AND player writes, including recovery after a native Editor crash.
	/// FishMMO's RenderMeshIndirect terrain rendering uses its own procedural shaders, not BRG.
	/// </summary>
	public sealed class WebGLRenderPipelineScope : IDisposable
	{
		private const string RecoveryPath = "Library/FishMMO/WebGLRenderPipelineRecovery.json";
		private const string DrawerField = "m_GPUResidentDrawerMode";
		private static int activeScopes;
		private readonly bool enabled, ownsRecovery;
		private readonly Recovery snapshot;
		private bool disposed;

		[Serializable] private sealed class Entry { public string guid; public int mode; }
		[Serializable] private sealed class Recovery { public int brg; public List<Entry> pipelines = new List<Entry>(); }

		[InitializeOnLoadMethod]
		private static void ScheduleRecovery() => EditorApplication.delayCall += RecoverInterruptedBuild;

		public static void RecoverInterruptedBuild()
		{
			if (activeScopes != 0 || !File.Exists(RecoveryPath)) return;
			try
			{
				Restore(JsonUtility.FromJson<Recovery>(File.ReadAllText(RecoveryPath)));
				AssetDatabase.SaveAssets();
				File.Delete(RecoveryPath);
				Debug.Log("Restored render pipeline settings after an interrupted FishMMO WebGPU build.");
			}
			catch (Exception e) { Debug.LogError("FishMMO WebGPU pipeline recovery failed: " + e); }
		}

		public WebGLRenderPipelineScope(bool enabled)
		{
			this.enabled = enabled;
			if (!enabled) return;
			// Batch entry points can run before the scheduled Editor delay callback.
			if (activeScopes == 0) RecoverInterruptedBuild();
			snapshot = new Recovery { brg = (int)EditorGraphicsSettings.batchRendererGroupShaderStrippingMode };
			var assets = new HashSet<RenderPipelineAsset> { GraphicsSettings.defaultRenderPipeline };
			for (int i = 0; i < QualitySettings.names.Length; i++) assets.Add(QualitySettings.GetRenderPipelineAssetAt(i));
			foreach (RenderPipelineAsset asset in assets)
			{
				if (asset == null) continue;
				using var serialized = new SerializedObject(asset);
				var mode = serialized.FindProperty(DrawerField);
				if (mode == null) continue;
				string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
				if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("Save render pipeline assets before building WebGPU.");
				snapshot.pipelines.Add(new Entry { guid = guid, mode = mode.intValue });
			}
			ownsRecovery = activeScopes == 0;
			if (ownsRecovery)
			{
				if (File.Exists(RecoveryPath)) throw new InvalidOperationException("Recover the interrupted WebGPU pipeline scope before building again: " + RecoveryPath);
				Directory.CreateDirectory(Path.GetDirectoryName(RecoveryPath));
				File.WriteAllText(RecoveryPath, JsonUtility.ToJson(snapshot));
			}
			activeScopes++;
			try
			{
				foreach (Entry entry in snapshot.pipelines) SetMode(entry.guid, 0);
				SetBrgMode(BatchRendererGroupStrippingMode.StripAll);
			}
			catch
			{
				try { Restore(snapshot); AssetDatabase.SaveAssets(); if (ownsRecovery) File.Delete(RecoveryPath); }
				finally { activeScopes--; }
				throw;
			}
		}

		private static void SetMode(string guid, int value)
		{
			var asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
			if (asset == null) throw new InvalidOperationException("Cannot restore render pipeline " + guid);
			using var serialized = new SerializedObject(asset);
			var mode = serialized.FindProperty(DrawerField);
			if (mode == null) throw new InvalidOperationException("Render pipeline drawer settings changed: " + guid);
			if (mode.intValue == value) return;
			mode.intValue = value;
			serialized.ApplyModifiedPropertiesWithoutUndo();
		}

		private static void SetBrgMode(BatchRendererGroupStrippingMode value)
		{
			var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
			if (settings.Length == 0) throw new InvalidOperationException("Graphics settings asset was not found.");
			using var serialized = new SerializedObject(settings[0]);
			var mode = serialized.FindProperty("m_BrgStripping");
			if (mode == null) throw new InvalidOperationException("BatchRendererGroup stripping settings changed.");
			if (mode.intValue == (int)value) return;
			mode.intValue = (int)value;
			serialized.ApplyModifiedPropertiesWithoutUndo();
		}

		private static void Restore(Recovery recovery)
		{
			if (recovery?.pipelines == null) throw new InvalidOperationException("Invalid WebGPU pipeline recovery snapshot.");
			foreach (Entry entry in recovery.pipelines) SetMode(entry.guid, entry.mode);
			SetBrgMode((BatchRendererGroupStrippingMode)recovery.brg);
		}

		public void Dispose()
		{
			if (disposed || !enabled) return;
			disposed = true;
			try
			{
				Restore(snapshot);
				AssetDatabase.SaveAssets();
				if (ownsRecovery) File.Delete(RecoveryPath);
			}
			finally { activeScopes--; }
		}
	}
}
#endif
