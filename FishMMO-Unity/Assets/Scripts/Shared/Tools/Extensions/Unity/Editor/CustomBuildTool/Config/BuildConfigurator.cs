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
        private const int MAX_WAIT_FRAMES = 600; // 10 seconds at 60fps, 20 seconds at 30fps

        /// <summary>
        /// Configures the Unity Editor and Player settings for the build process, saving the current state for later restoration.
        /// </summary>
        public void Configure()
        {
            if (isConfigured)
            {
                Log.Warning("BuildConfigurator", "Configure() called but already configured. Skipping.");
                return;
            }

            Log.Debug("BuildConfigurator", "Saving current build and player settings, and applying build configuration.");
            
            // Save all pending changes before switching build targets
            AssetDatabase.SaveAssets();
            
            PushSettings(EditorUserBuildSettings.activeBuildTarget);
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
        /// </summary>
        /// <param name="buildTarget">The build target to switch to.</param>
        private void PushSettings(BuildTarget buildTarget)
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

            // Switch active build target and WAIT for completion
            BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
            
            // If already at the target, no need to switch
            if (EditorUserBuildSettings.activeBuildTarget == buildTarget)
            {
                Log.Debug("BuildConfigurator", $"Already at build target {buildTarget}, no switch needed.");
            }
            else
            {
                Log.Debug("BuildConfigurator", $"Switching build target from {originalBuildTarget} to {buildTarget}...");
                bool switchResult = EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, buildTarget);
                
                if (!switchResult)
                {
                    Log.Warning("BuildConfigurator", $"SwitchActiveBuildTarget returned false for {buildTarget}. Target may not be installed.");
                }
                
                // CRITICAL: Wait for the build target switch to complete
                // SwitchActiveBuildTarget is asynchronous and returns immediately
                WaitForBuildTargetSwitch(buildTarget, targetGroup);
            }

            // Set subtarget for standalone
            if (buildTarget == BuildTarget.StandaloneWindows64 || buildTarget == BuildTarget.StandaloneLinux64 || buildTarget == BuildTarget.StandaloneOSX)
            {
                EditorUserBuildSettings.standaloneBuildSubtarget = originalBuildSubtarget;
            }
            else
            {
                EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
            }

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
            Log.Debug("BuildConfigurator", $"Build target switch to {buildTarget} completed successfully.");
        }
        
        /// <summary>
        /// Waits for the build target switch to complete.
        /// SwitchActiveBuildTarget is async and doesn't provide callbacks.
        /// </summary>
        private void WaitForBuildTargetSwitch(BuildTarget targetBuildTarget, BuildTargetGroup targetGroup)
        {
            // If already at target, return immediately
            if (EditorUserBuildSettings.activeBuildTarget == targetBuildTarget)
            {
                Log.Debug("BuildConfigurator", "Build target is already set correctly.");
                return;
            }

            int frameCount = 0;
            bool switchCompleted = false;
            EditorApplication.CallbackFunction updateCallback = null;
            
            updateCallback = () =>
            {
                frameCount++;
                
                // Check if switch completed
                if (EditorUserBuildSettings.activeBuildTarget == targetBuildTarget)
                {
                    switchCompleted = true;
                    EditorApplication.update -= updateCallback;
                    Log.Debug("BuildConfigurator", $"Build target switch completed after {frameCount} editor updates.");
                    return;
                }
                
                // Log progress every 60 updates (~1 second)
                if (frameCount % 60 == 0)
                {
                    Log.Debug("BuildConfigurator", $"Waiting for build target switch... (update {frameCount})");
                }
                
                // Timeout check (600 updates = ~10 seconds)
                if (frameCount >= MAX_WAIT_FRAMES)
                {
                    EditorApplication.update -= updateCallback;
                    string errorMsg = $"Build target switch timed out after {frameCount} updates. " +
                        $"Current: {EditorUserBuildSettings.activeBuildTarget}, Expected: {targetBuildTarget}";
                    Log.Error("BuildConfigurator", errorMsg);
                    throw new System.TimeoutException($"Failed to switch to build target {targetBuildTarget}");
                }
            };
            
            EditorApplication.update += updateCallback;
            
            // Wait for switch to complete by spinning (allowing Unity's update to be called)
            while (!switchCompleted && frameCount < MAX_WAIT_FRAMES)
            {
                // Yield to prevent tight loop - allows Unity's event loop to process
                System.Threading.Thread.Sleep(1);
            }
            
            // Cleanup
            EditorApplication.update -= updateCallback;
            
            if (!switchCompleted)
            {
                string errorMsg = $"Build target switch failed. " +
                    $"Current: {EditorUserBuildSettings.activeBuildTarget}, Expected: {targetBuildTarget}";
                Log.Error("BuildConfigurator", errorMsg);
                throw new System.TimeoutException($"Failed to switch to build target {targetBuildTarget}");
            }
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
            
            // Switch back to original build target and WAIT for completion
            Log.Debug("BuildConfigurator", $"Restoring build target to {originalBuildTarget}...");
            bool switchResult = EditorUserBuildSettings.SwitchActiveBuildTarget(originalGroup, originalBuildTarget);
            
            if (!switchResult)
            {
                Log.Warning("BuildConfigurator", "SwitchActiveBuildTarget (restore) returned false. Target may already be active.");
            }
            
            // CRITICAL: Wait for the restore to complete
            WaitForBuildTargetSwitch(originalBuildTarget, originalGroup);
            
            EditorUserBuildSettings.standaloneBuildSubtarget = originalBuildSubtarget;
            
            // Save restored settings
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            Log.Debug("BuildConfigurator", "Build target restored successfully.");
            
            ForceEditorScriptRecompile();
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
                    Log.Debug("BuildConfigurator", $"Forcing editor recompile by reimporting: {scriptPath}");
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