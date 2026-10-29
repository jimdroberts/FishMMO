#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Shared.CustomBuildTool.Core
{
	/// <summary>
	/// Headless content does not use a render pipeline. Detach client pipelines before
	/// SBP constructs URP build callbacks (which happens before shader preprocessing).
	/// Keep the pipeline assets themselves untouched and restore every quality override.
	/// </summary>
	public sealed class ServerRenderPipelineScope : IDisposable
	{
		private readonly RenderPipelineAsset defaultPipeline;
		private readonly RenderPipelineAsset[] qualityPipelines;
		private static int activeScopes;
		private readonly bool ownsRecovery;
		private bool disposed;
		private const string RecoveryPath = "Library/FishMMO/ServerRenderPipelineRecovery.json";

		[Serializable]
		private sealed class Recovery
		{
			public string defaultPipeline;
			public string[] qualityPipelines;
			public string[] qualityNames;
		}

		[InitializeOnLoadMethod]
		private static void ScheduleRecovery() => EditorApplication.delayCall += RecoverInterruptedBuild;

		/// <summary>A native crash skips Dispose. Recover the saved asset references on next editor startup.</summary>
		public static void RecoverInterruptedBuild()
		{
			if (activeScopes != 0 || !File.Exists(RecoveryPath)) return;
			try
			{
				var recovery = JsonUtility.FromJson<Recovery>(File.ReadAllText(RecoveryPath));
				string[] names = QualitySettings.names;
				if (recovery?.qualityNames == null || recovery.qualityPipelines == null ||
					recovery.qualityNames.Length != names.Length || recovery.qualityPipelines.Length != names.Length)
					throw new InvalidOperationException("Quality levels changed; recovery snapshot retained at " + RecoveryPath);
				for (int index = 0; index < names.Length; index++)
					if (names[index] != recovery.qualityNames[index])
						throw new InvalidOperationException("Quality level names changed; recovery snapshot retained at " + RecoveryPath);
				var pipelines = new RenderPipelineAsset[names.Length];
				for (int index = 0; index < pipelines.Length; index++)
					pipelines[index] = QualitySettings.GetRenderPipelineAssetAt(index) ?? LoadPipeline(recovery.qualityPipelines[index]);
				if (GraphicsSettings.defaultRenderPipeline == null)
					GraphicsSettings.defaultRenderPipeline = LoadPipeline(recovery.defaultPipeline);
				SetQualityPipelines(pipelines);
				AssetDatabase.SaveAssets();
				File.Delete(RecoveryPath);
				Debug.Log("Restored render pipelines after an interrupted FishMMO server content build.");
			}
			catch (Exception exception) { Debug.LogError("FishMMO render pipeline recovery failed: " + exception); }
		}

		private static RenderPipelineAsset LoadPipeline(string guid)
		{
			if (string.IsNullOrEmpty(guid)) return null;
			var asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
			if (asset == null) throw new InvalidOperationException("Cannot restore render pipeline " + guid);
			return asset;
		}

		private static string PipelineGuid(RenderPipelineAsset pipeline) =>
			AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(pipeline));

		public ServerRenderPipelineScope()
		{
			defaultPipeline = GraphicsSettings.defaultRenderPipeline;
			qualityPipelines = new RenderPipelineAsset[QualitySettings.names.Length];
			for (int index = 0; index < qualityPipelines.Length; index++)
				qualityPipelines[index] = QualitySettings.GetRenderPipelineAssetAt(index);
			ownsRecovery = activeScopes == 0;
			if (ownsRecovery)
			{
				if (File.Exists(RecoveryPath))
					throw new InvalidOperationException("An interrupted build needs render pipeline recovery before another server build: " + RecoveryPath);
				var snapshot = new Recovery { defaultPipeline = PipelineGuid(defaultPipeline),
					qualityPipelines = Array.ConvertAll(qualityPipelines, PipelineGuid), qualityNames = QualitySettings.names };
				Directory.CreateDirectory(Path.GetDirectoryName(RecoveryPath));
				File.WriteAllText(RecoveryPath, JsonUtility.ToJson(snapshot));
			}
			activeScopes++;
			try
			{
				GraphicsSettings.defaultRenderPipeline = null;
				SetQualityPipelines(new RenderPipelineAsset[qualityPipelines.Length]);
			}
			catch
			{
				try
				{
					Restore();
					if (ownsRecovery) File.Delete(RecoveryPath);
				}
				finally { activeScopes--; }
				throw;
			}
		}

		private static void SetQualityPipelines(RenderPipelineAsset[] pipelines)
		{
			int selected = QualitySettings.GetQualityLevel();
			try
			{
				for (int index = 0; index < pipelines.Length; index++)
				{
					QualitySettings.SetQualityLevel(index, false);
					QualitySettings.renderPipeline = pipelines[index];
				}
			}
			finally { QualitySettings.SetQualityLevel(selected, false); }
		}

		private void Restore()
		{
			GraphicsSettings.defaultRenderPipeline = defaultPipeline;
			SetQualityPipelines(qualityPipelines);
		}

		private bool IsRestored()
		{
			if (!ReferenceEquals(GraphicsSettings.defaultRenderPipeline, defaultPipeline)) return false;
			for (int index = 0; index < qualityPipelines.Length; index++)
				if (!ReferenceEquals(QualitySettings.GetRenderPipelineAssetAt(index), qualityPipelines[index])) return false;
			return true;
		}

		public void Dispose()
		{
			if (disposed) return;
			disposed = true;
			try
			{
				Restore();
				// SaveAssets may have persisted the temporary null settings during SBP.
				AssetDatabase.SaveAssets();
				// Saving writes the settings files and re-imports them. A pipeline that is not an asset on
				// disk cannot be written, so it reads back as none: put it back in memory too.
				if (!IsRestored()) Restore();
				if (ownsRecovery) File.Delete(RecoveryPath);
			}
			finally { activeScopes--; }
		}
	}
}
#endif
