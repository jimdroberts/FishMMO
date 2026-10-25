using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
public static class BuildProbe
{
    public static void Build()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Probe.unity");
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneLinux64);
        EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Server;
        foreach (var backend in new[] { ScriptingImplementation.Mono2x, ScriptingImplementation.IL2CPP })
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Server, backend);
            PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.Server, Il2CppCodeGeneration.OptimizeSize);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Server, ManagedStrippingLevel.Low);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Server, ApiCompatibilityLevel.NET_Standard);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Probe.unity" },
                locationPathName = Path.GetFullPath(Path.Combine("Builds", backend.ToString(), "Probe.x86_64")),
                target = BuildTarget.StandaloneLinux64,
                subtarget = (int)StandaloneBuildSubtarget.Server,
                options = BuildOptions.Development | BuildOptions.CleanBuildCache
            });
            if (report.summary.result != BuildResult.Succeeded) { EditorApplication.Exit(1); return; }
        }
        EditorApplication.Exit(0);
    }
}
