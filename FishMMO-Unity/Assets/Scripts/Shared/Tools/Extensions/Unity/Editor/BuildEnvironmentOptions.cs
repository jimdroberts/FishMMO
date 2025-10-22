using UnityEditor;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents the build type for the project (Server or Client).
	/// </summary>
	public enum BuildTypeEnvironment
	{
		/// <summary>Server build type.</summary>
		Server = 0,
		/// <summary>Client build type.</summary>
		Client = 1,
	}

	/// <summary>
	/// Represents the operating system target for builds.
	/// </summary>
	public enum OSTargetEnvironment
	{
		/// <summary>Windows x64 build target.</summary>
		Windows = 0,
		/// <summary>Linux x64 build target.</summary>
		Linux = 1,
		/// <summary>WebGL build target.</summary>
		WebGL = 2,
	}

	/// <summary>
	/// Provides menu options and utilities for managing build environment settings (Build Type and OS Target).
	/// </summary>
	[InitializeOnLoad]
	public class BuildEnvironmentOptions
	{
		private const string PREF_BUILD_TYPE = "FishMMOBuildType";
		private const string PREF_OS_TARGET = "FishMMOOSTarget";

		#region Build Type Menu Items

		/// <summary>
		/// Sets the build type to Server and switches the Unity Editor to the appropriate build target.
		/// </summary>
		[MenuItem("FishMMO/Build/Build Type/Server")]
		static void SetBuildTypeServer()
		{
			EditorPrefs.SetInt(PREF_BUILD_TYPE, (int)BuildTypeEnvironment.Server);
			SwitchToEnvironmentBuildTarget();
		}

		/// <summary>
		/// Sets the build type to Client and switches the Unity Editor to the appropriate build target.
		/// </summary>
		[MenuItem("FishMMO/Build/Build Type/Client")]
		static void SetBuildTypeClient()
		{
			EditorPrefs.SetInt(PREF_BUILD_TYPE, (int)BuildTypeEnvironment.Client);
			SwitchToEnvironmentBuildTarget();
		}

		/// <summary>
		/// Validation method for build type menu. Updates checkmarks based on current build type.
		/// </summary>
		[MenuItem("FishMMO/Build/Build Type/Server", true)]
		static bool ValidateBuildType()
		{
			Menu.SetChecked("FishMMO/Build/Build Type/Server", false);
			Menu.SetChecked("FishMMO/Build/Build Type/Client", false);

			BuildTypeEnvironment buildType = GetBuildType();
			switch (buildType)
			{
				case BuildTypeEnvironment.Server:
					Menu.SetChecked("FishMMO/Build/Build Type/Server", true);
					break;
				case BuildTypeEnvironment.Client:
					Menu.SetChecked("FishMMO/Build/Build Type/Client", true);
					break;
			}
			return true;
		}

		#endregion

		#region OS Target Menu Items

		/// <summary>
		/// Sets the OS target to Windows x64 and switches the Unity Editor to the appropriate build target.
		/// </summary>
		[MenuItem("FishMMO/Build/OS Target/Windows x64")]
		static void SetOSTargetWindows()
		{
			EditorPrefs.SetInt(PREF_OS_TARGET, (int)OSTargetEnvironment.Windows);
			SwitchToEnvironmentBuildTarget();
		}

		/// <summary>
		/// Sets the OS target to Linux x64 and switches the Unity Editor to the appropriate build target.
		/// </summary>
		[MenuItem("FishMMO/Build/OS Target/Linux x64")]
		static void SetOSTargetLinux()
		{
			EditorPrefs.SetInt(PREF_OS_TARGET, (int)OSTargetEnvironment.Linux);
			SwitchToEnvironmentBuildTarget();
		}

		/// <summary>
		/// Sets the OS target to WebGL and switches the Unity Editor to the appropriate build target.
		/// </summary>
		[MenuItem("FishMMO/Build/OS Target/WebGL")]
		static void SetOSTargetWebGL()
		{
			EditorPrefs.SetInt(PREF_OS_TARGET, (int)OSTargetEnvironment.WebGL);
			SwitchToEnvironmentBuildTarget();
		}

		/// <summary>
		/// Validation method for OS target menu. Updates checkmarks based on current OS target.
		/// </summary>
		[MenuItem("FishMMO/Build/OS Target/Windows x64", true)]
		static bool ValidateOSTarget()
		{
			Menu.SetChecked("FishMMO/Build/OS Target/Windows x64", false);
			Menu.SetChecked("FishMMO/Build/OS Target/Linux x64", false);
			Menu.SetChecked("FishMMO/Build/OS Target/WebGL", false);

			OSTargetEnvironment osTarget = GetOSTarget();
			switch (osTarget)
			{
				case OSTargetEnvironment.Windows:
					Menu.SetChecked("FishMMO/Build/OS Target/Windows x64", true);
					break;
				case OSTargetEnvironment.Linux:
					Menu.SetChecked("FishMMO/Build/OS Target/Linux x64", true);
					break;
				case OSTargetEnvironment.WebGL:
					Menu.SetChecked("FishMMO/Build/OS Target/WebGL", true);
					break;
			}
			return true;
		}

		#endregion

		#region Public API

		/// <summary>
		/// Gets the current build type environment (Server or Client).
		/// </summary>
		/// <returns>The current BuildTypeEnvironment.</returns>
		public static BuildTypeEnvironment GetBuildType()
		{
			return (BuildTypeEnvironment)EditorPrefs.GetInt(PREF_BUILD_TYPE, (int)BuildTypeEnvironment.Server);
		}

		/// <summary>
		/// Gets the current OS target environment (Windows, Linux, or WebGL).
		/// </summary>
		/// <returns>The current OSTargetEnvironment.</returns>
		public static OSTargetEnvironment GetOSTarget()
		{
			return (OSTargetEnvironment)EditorPrefs.GetInt(PREF_OS_TARGET, (int)OSTargetEnvironment.Windows);
		}

		/// <summary>
		/// Converts the OS target environment to Unity BuildTarget.
		/// </summary>
		/// <param name="osTarget">The OS target to convert.</param>
		/// <returns>The corresponding Unity BuildTarget.</returns>
		public static BuildTarget GetBuildTarget(OSTargetEnvironment osTarget)
		{
			switch (osTarget)
			{
				case OSTargetEnvironment.Windows:
					return BuildTarget.StandaloneWindows64;
				case OSTargetEnvironment.Linux:
					return BuildTarget.StandaloneLinux64;
				case OSTargetEnvironment.WebGL:
					return BuildTarget.WebGL;
				default:
					return BuildTarget.StandaloneWindows64;
			}
		}

		/// <summary>
		/// Gets the Unity BuildTarget based on the current OS target environment.
		/// </summary>
		/// <returns>The current Unity BuildTarget.</returns>
		public static BuildTarget GetCurrentBuildTarget()
		{
			return GetBuildTarget(GetOSTarget());
		}

		/// <summary>
		/// Converts the build type environment to Unity StandaloneBuildSubtarget.
		/// </summary>
		/// <param name="buildType">The build type to convert.</param>
		/// <returns>The corresponding Unity StandaloneBuildSubtarget.</returns>
		public static StandaloneBuildSubtarget GetBuildSubtarget(BuildTypeEnvironment buildType)
		{
			switch (buildType)
			{
				case BuildTypeEnvironment.Server:
					return StandaloneBuildSubtarget.Server;
				case BuildTypeEnvironment.Client:
					return StandaloneBuildSubtarget.Player;
				default:
					return StandaloneBuildSubtarget.Player;
			}
		}

		/// <summary>
		/// Gets the Unity StandaloneBuildSubtarget based on the current build type environment.
		/// </summary>
		/// <returns>The current Unity StandaloneBuildSubtarget.</returns>
		public static StandaloneBuildSubtarget GetCurrentBuildSubtarget()
		{
			return GetBuildSubtarget(GetBuildType());
		}

		/// <summary>
		/// Gets the CustomBuildType based on the current build type environment.
		/// </summary>
		/// <returns>The current CustomBuildType.</returns>
		public static CustomBuildTool.CustomBuildType GetCustomBuildType()
		{
			BuildTypeEnvironment buildType = GetBuildType();
			return buildType == BuildTypeEnvironment.Server
				? CustomBuildTool.CustomBuildType.Server
				: CustomBuildTool.CustomBuildType.Client;
		}

		/// <summary>
		/// Switches the Unity Editor to the build target specified in the environment options.
		/// Forces asset reimport and script recompilation immediately.
		/// </summary>
		/// <returns>True if the switch was successful or already at target, false otherwise.</returns>
		public static bool SwitchToEnvironmentBuildTarget()
		{
			BuildTarget targetBuild = GetCurrentBuildTarget();
			StandaloneBuildSubtarget targetSubtarget = GetCurrentBuildSubtarget();

			// Check if already at target
			if (EditorUserBuildSettings.activeBuildTarget == targetBuild &&
				EditorUserBuildSettings.standaloneBuildSubtarget == targetSubtarget)
			{
				UnityEngine.Debug.Log($"[BuildEnvironmentOptions] Already at target: {targetBuild}:{targetSubtarget}");
				return true;
			}

			// Switch the build target immediately
			UnityEngine.Debug.Log($"[BuildEnvironmentOptions] Switching to: {targetBuild}:{targetSubtarget}");
			BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(targetBuild);
			bool result = EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, targetBuild);

			if (result)
			{
				// Set the subtarget
				EditorUserBuildSettings.standaloneBuildSubtarget = targetSubtarget;

				// Force asset database refresh and reimport
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

				// Force script recompilation (non-blocking)
				ForceEditorScriptRecompile();

				UnityEngine.Debug.Log($"[BuildEnvironmentOptions] Build target switched to: {targetBuild}:{targetSubtarget}. Scripts will recompile automatically.");
			}
			else
			{
				UnityEngine.Debug.LogWarning($"[BuildEnvironmentOptions] Failed to switch to: {targetBuild}:{targetSubtarget}. Target module may not be installed.");
			}

			return result;
		}

		/// <summary>
		/// Gets whether scripts are currently compiling in the Unity Editor.
		/// </summary>
		/// <returns>True if scripts are compiling, false otherwise.</returns>
		public static bool IsCompiling()
		{
			return EditorApplication.isCompiling;
		}

		/// <summary>
		/// Forces the Unity Editor to recompile scripts by reimporting a script asset.
		/// </summary>
		private static void ForceEditorScriptRecompile()
		{
			string[] allScriptGuids = AssetDatabase.FindAssets("t:Script");
			if (allScriptGuids.Length > 0)
			{
				string scriptPath = AssetDatabase.GUIDToAssetPath(allScriptGuids[0]);
				if (System.IO.File.Exists(scriptPath))
				{
					UnityEngine.Debug.Log($"[BuildEnvironmentOptions] Forcing editor script recompile via: {scriptPath}");
					AssetDatabase.ImportAsset(scriptPath, ImportAssetOptions.ForceUpdate);
				}
				else
				{
					UnityEngine.Debug.LogWarning($"[BuildEnvironmentOptions] Found script GUID but file does not exist to reimport. Define symbols might not update as expected.");
				}
			}
			else
			{
				UnityEngine.Debug.LogWarning($"[BuildEnvironmentOptions] No script files found to force editor recompile. Define symbols might not update.");
			}
		}

		#endregion
	}
}