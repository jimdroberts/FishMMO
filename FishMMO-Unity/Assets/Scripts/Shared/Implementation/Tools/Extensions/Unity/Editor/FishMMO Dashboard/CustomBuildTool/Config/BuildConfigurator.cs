#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
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
				ApplyBuildTargetSettings(buildSubtarget, buildTarget, targetGroup);
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

			// IL2CPP cross-compilation from Linux → Windows is not supported.
			// If the current scripting backend is IL2CPP and the target platform
			// cannot be built with IL2CPP on this editor, force Mono to keep the
			// SBP CanBuildPlayer check happy.
			if (buildTarget != BuildTarget.WebGL && buildSubtarget != StandaloneBuildSubtarget.Server)
			{
				var currentBackend = PlayerSettings.GetScriptingBackend(currentNamedBuildTargetGroup);
				if (currentBackend == ScriptingImplementation.IL2CPP && !CanIL2CPPCrossCompile(buildTarget))
				{
					PlayerSettings.SetScriptingBackend(currentNamedBuildTargetGroup, ScriptingImplementation.Mono2x);
					Log.Warning("BuildConfigurator", $"IL2CPP is not available for {buildTarget} on this editor platform. Falling back to Mono.");
				}
			}

			// Apply the dedicated-server scripting backend selected in the FishMMO Dashboard
			// ("Dedicated Server: Use IL2CPP", persisted as the EditorPref
			// BuildEnvironmentOptions.PREF_SERVER_USE_IL2CPP). The Server subtarget has its
			// own NamedBuildTarget distinct from the Standalone Player one, so the backend
			// must be configured explicitly for both enabled and disabled states.
			//
			if (buildTarget != BuildTarget.WebGL && buildSubtarget == StandaloneBuildSubtarget.Server)
			{
				bool useIl2Cpp = EditorPrefs.GetBool(BuildEnvironmentOptions.PREF_SERVER_USE_IL2CPP, false);
				BuildEnvironmentOptions.ApplyServerScriptingBackend(useIl2Cpp);
			}

			// Save PlayerSettings changes without forcing a reimport loop during builds.
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

			Log.Debug("BuildConfigurator", "Build target restored successfully.");
		}

		/// <summary>
		/// Returns true if IL2CPP cross-compilation from the current editor platform
		/// to the given build target is expected to work. On Linux, IL2CPP cannot
		/// cross-compile to Windows; on Windows, IL2CPP cannot cross-compile to Linux.
		/// </summary>
		private static bool CanIL2CPPCrossCompile(BuildTarget target)
		{
#if UNITY_EDITOR_LINUX
			// Linux → Windows IL2CPP cross-compilation is not supported
			if (target == BuildTarget.StandaloneWindows64)
				return false;
#elif UNITY_EDITOR_WIN
			// Windows → Linux IL2CPP cross-compilation is not supported
			if (target == BuildTarget.StandaloneLinux64)
				return false;
#endif
			return true;
		}
	}
}
#endif