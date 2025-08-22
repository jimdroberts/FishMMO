#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using System.IO;
using FishMMO.Logging;

namespace FishMMO.Shared.CustomBuildTool
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

        /// <summary>
        /// Configures the Unity Editor and Player settings for the build process, saving the current state for later restoration.
        /// </summary>
        public void Configure()
        {
            Log.Debug("BuildConfigurator", "Saving current build and player settings, and applying build configuration.");
            PushSettings(EditorUserBuildSettings.activeBuildTarget);
        }

        /// <summary>
        /// Restores the Unity Editor and Player settings to their original state after the build process.
        /// </summary>
        public void Restore()
        {
            Log.Debug("BuildConfigurator", "Restoring original build and player settings.");
            PopSettings();
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

            // Switch active build target
            BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(buildTarget);
            EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, buildTarget);

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
            EditorUserBuildSettings.SwitchActiveBuildTarget(originalGroup, originalBuildTarget);
            EditorUserBuildSettings.standaloneBuildSubtarget = originalBuildSubtarget;
            AssetDatabase.Refresh();
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