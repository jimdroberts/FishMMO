#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using System.IO;
using FishMMO.Logging;
using FishMMO.Shared.CustomBuildTool.Core;

namespace FishMMO.Shared.CustomBuildTool.Config
{
	/// <summary>
	/// Handles configuration and restoration of Unity build and player settings for custom build processes.
	/// </summary>
	public class BuildConfigurator : IBuildConfigurator
	{
		// Fields to store original settings for restoration
		private BuildTargetGroup originalGroup;
		private BuildTarget originalBuildTarget;
		private StandaloneBuildSubtarget originalBuildSubtarget;
		private ScriptingImplementation originalScriptingImp;
		private Il2CppCompilerConfiguration originalCompilerConf;
		private Il2CppCodeGeneration originalOptimization;
		private bool originalBakeCollisionMeshes;
		private bool originalStripUnusedMeshComponents;
		private WebGLCompressionFormat originalCompressionFormat;
		private bool originalDecompressionFallback;
		private bool originalDataCaching;

		private bool isConfigured = false;

		/// <summary>
		/// Configures the Unity Editor and Player settings for the build process, saving the current state for later restoration.
		/// Attempts to switch to the target build platform if it differs from the current platform.
		/// </summary>
		/// <param name="targetSubtarget">The build subtarget to switch to.</param>
		/// <param name="targetBuildTarget">The build target to switch to.</param>
		public void Configure(StandaloneBuildSubtarget targetSubtarget, BuildTarget targetBuildTarget)
		{
			if (isConfigured)
			{
				Log.Warning("BuildConfigurator", "Configure() called but already configured. Skipping.");
				return;
			}

			Log.Debug("BuildConfigurator", "Saving current build and player settings, and applying build configuration.");

			// Save all pending changes before switching build targets
			AssetDatabase.SaveAssets();

			PushSettings(targetSubtarget, targetBuildTarget);
			isConfigured = true;
		}

		/// <summary>
		/// Restores the Unity Editor and Player settings to their original state after the build process.
		/// </summary>
		public void Restore()
		{
			if (!isConfigured)
			{
				Log.Warning("BuildConfigurator", "Restore() called but Configure() was never called or failed. Skipping restore.");
				return;
			}

			try
			{
				Log.Debug("BuildConfigurator", "Restoring original build and player settings.");
				PopSettings();
			}
			catch (System.Exception ex)
			{
				Log.Error("BuildConfigurator", $"Error during settings restoration: {ex.Message}");
				throw;
			}
			finally
			{
				isConfigured = false;
			}
		}

		/// <summary>
		/// Saves the current build and player settings, then switches to the specified build target.
		/// If the switch fails, uses the current build target as a fallback.
		/// </summary>
		/// <param name="buildSubtarget">The build subtarget to switch to.</param>
		/// <param name="buildTarget">The build target to switch to.</param>
		private void PushSettings(StandaloneBuildSubtarget buildSubtarget, BuildTarget buildTarget)
		{
			originalGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
			originalBuildTarget = EditorUserBuildSettings.activeBuildTarget;
			originalBuildSubtarget = EditorUserBuildSettings.standaloneBuildSubtarget;

			var originalNamedBuildTargetGroup = NamedBuildTarget.FromBuildTargetGroup(originalGroup);
			originalScriptingImp = PlayerSettings.GetScriptingBackend(originalNamedBuildTargetGroup);
			originalCompilerConf = PlayerSettings.GetIl2CppCompilerConfiguration(originalNamedBuildTargetGroup);
			originalOptimization = PlayerSettings.GetIl2CppCodeGeneration(originalNamedBuildTargetGroup);
			originalBakeCollisionMeshes = PlayerSettings.bakeCollisionMeshes;
			originalStripUnusedMeshComponents = PlayerSettings.stripUnusedMeshComponents;
			originalCompressionFormat = PlayerSettings.WebGL.compressionFormat;
			originalDecompressionFallback = PlayerSettings.WebGL.decompressionFallback;
			originalDataCaching = PlayerSettings.WebGL.dataCaching;

			// Switch active build target
			BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);

			// If already at the target, no need to switch
			if (EditorUserBuildSettings.activeBuildTarget == buildTarget && EditorUserBuildSettings.standaloneBuildSubtarget == buildSubtarget)
			{
				Log.Debug("BuildConfigurator", $"Already at build target {buildTarget} with subtarget {buildSubtarget}, no switch needed.");
			}
			else
			{
				Log.Debug("BuildConfigurator", $"Switching build target from {originalBuildTarget}:{originalBuildSubtarget} to {buildTarget}:{buildSubtarget}...");

				bool switchResult = EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, buildTarget);

				if (!switchResult)
				{
					Log.Warning("BuildConfigurator", $"SwitchActiveBuildTarget returned false for {buildTarget}:{buildSubtarget}. Target may not be installed.");
					Log.Warning("BuildConfigurator", $"Using current build target {originalBuildTarget}:{originalBuildSubtarget} as fallback.");
				}
				else
				{
					ApplyBuildTargetSettings(buildSubtarget, buildTarget, targetGroup);

					// Force asset database refresh and reimport
					AssetDatabase.SaveAssets();
					AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

					// Force script recompilation after platform switch
					ForceEditorScriptRecompile();

					Log.Debug("BuildConfigurator", $"Build target switched to {buildTarget}:{buildSubtarget} successfully.");
				}
			}
		}

		/// <summary>
		/// Applies build-specific settings after target switch completes.
		/// </summary>
		/// <param name="buildSubtarget">The build subtarget to apply.</param>
		/// <param name="buildTarget">The build target to apply settings for.</param>
		/// <param name="targetGroup">The build target group.</param>
		private void ApplyBuildTargetSettings(StandaloneBuildSubtarget buildSubtarget, BuildTarget buildTarget, BuildTargetGroup targetGroup)
		{
			// Set subtarget for standalone
			EditorUserBuildSettings.standaloneBuildSubtarget = buildSubtarget;

			// Apply desired settings for WebGL
			var currentNamedBuildTargetGroup = NamedBuildTarget.FromBuildTargetGroup(targetGroup);
			if (buildTarget == BuildTarget.WebGL)
			{
				PlayerSettings.SetScriptingBackend(currentNamedBuildTargetGroup, ScriptingImplementation.IL2CPP);
				PlayerSettings.SetIl2CppCompilerConfiguration(currentNamedBuildTargetGroup, Il2CppCompilerConfiguration.Release);
				PlayerSettings.SetIl2CppCodeGeneration(currentNamedBuildTargetGroup, Il2CppCodeGeneration.OptimizeSize);
				PlayerSettings.bakeCollisionMeshes = false;
				PlayerSettings.stripUnusedMeshComponents = false;
				PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
				PlayerSettings.WebGL.decompressionFallback = true;
				PlayerSettings.WebGL.dataCaching = true;
			}

			// Save changes after applying new settings
			AssetDatabase.SaveAssets();
			Log.Debug("BuildConfigurator", $"Build target configuration applied successfully for {buildTarget}:{buildSubtarget}.");
		}

		/// <summary>
		/// Restores the original Editor and Player settings after a build operation.
		/// </summary>
		private void PopSettings()
		{
			var originalNamedBuildTargetGroup = NamedBuildTarget.FromBuildTargetGroup(originalGroup);
			PlayerSettings.SetScriptingBackend(originalNamedBuildTargetGroup, originalScriptingImp);
			PlayerSettings.SetIl2CppCompilerConfiguration(originalNamedBuildTargetGroup, originalCompilerConf);
			PlayerSettings.SetIl2CppCodeGeneration(originalNamedBuildTargetGroup, originalOptimization);
			PlayerSettings.bakeCollisionMeshes = originalBakeCollisionMeshes;
			PlayerSettings.stripUnusedMeshComponents = originalStripUnusedMeshComponents;
			PlayerSettings.WebGL.compressionFormat = originalCompressionFormat;
			PlayerSettings.WebGL.decompressionFallback = originalDecompressionFallback;
			PlayerSettings.WebGL.dataCaching = originalDataCaching;

			// Switch back to original build target
			Log.Debug("BuildConfigurator", $"Restoring build target to {originalBuildTarget}...");

			// If already at original target, no need to switch
			if (EditorUserBuildSettings.activeBuildTarget == originalBuildTarget && EditorUserBuildSettings.standaloneBuildSubtarget == originalBuildSubtarget)
			{
				Log.Debug("BuildConfigurator", $"Already at original build target {originalBuildTarget}:{originalBuildSubtarget}, no switch needed.");
			}
			else
			{
				bool switchResult = EditorUserBuildSettings.SwitchActiveBuildTarget(originalGroup, originalBuildTarget);

				if (!switchResult)
				{
					Log.Warning("BuildConfigurator", "SwitchActiveBuildTarget (restore) returned false. Target may already be active.");
				}
				else
				{
					EditorUserBuildSettings.standaloneBuildSubtarget = originalBuildSubtarget;

					Log.Debug("BuildConfigurator", $"Build target restored to {originalBuildTarget}:{originalBuildSubtarget} successfully.");

					// Force asset database refresh and reimport
					AssetDatabase.SaveAssets();
					AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

					// Force script recompilation after platform switch
					ForceEditorScriptRecompile();
				}
			}

			// Save restored settings
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			Log.Debug("BuildConfigurator", "Build target restored successfully.");
		}

		/// <summary>
		/// Forces the Unity Editor to recompile scripts by reimporting a script asset.
		/// </summary>
		private void ForceEditorScriptRecompile()
		{
			string[] allScriptGuids = AssetDatabase.FindAssets("t:Script");
			if (allScriptGuids.Length > 0)
			{
				string scriptPath = AssetDatabase.GUIDToAssetPath(allScriptGuids[0]);
				if (File.Exists(scriptPath))
				{
					Log.Debug("BuildConfigurator", $"Forcing editor script recompile.");
					AssetDatabase.ImportAsset(scriptPath, ImportAssetOptions.ForceUpdate);
				}
				else
				{
					Log.Warning("BuildConfigurator", "Found script GUID but file does not exist to reimport. Define symbols might not update as expected.");
				}
			}
			else
			{
				Log.Warning("BuildConfigurator", "No script files found to force editor recompile. Define symbols might not update.");
			}
		}
	}
}
#endif