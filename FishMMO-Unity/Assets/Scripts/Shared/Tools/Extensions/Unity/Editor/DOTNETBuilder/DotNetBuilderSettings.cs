using UnityEngine;
using System.IO;

namespace FishMMO.Shared
{
	// This ScriptableObject will hold all the settings for building the .NET Class Library.
	// It can be created as an asset in the Unity project (Assets > Create > FishMMO > Build Tools > DotNet Build Settings).
	[CreateAssetMenu(fileName = "New DotNet Build Settings", menuName = "FishMMO/Build Tools/DotNet Build Settings")]
	public class DotNetBuildSettings : ScriptableObject
	{
		[Tooltip("Path to your .NET Project's .csproj file, relative to your Unity project's root.")]
		public string PathToClassLibraryCsproj = "../MySharedLibrary/MySharedLibrary.csproj";

		[Tooltip("The output directory for the compiled DLLs, relative to your Unity project's Assets folder (e.g., 'Assets/Plugins/MySharedLibrary'). This is used only if 'Use Default Output Path' is false.")]
		public string OutputDirectory = "Assets/Plugins/MySharedLibrary";

		[Tooltip("If true, the output DLLs will be placed in the default build output path relative to the .csproj file (typically bin/[Configuration]/[TargetFramework]).")]
		public bool UseDefaultOutputPath = false;

		[Tooltip(".NET build configuration (e.g., 'Debug', 'Release').")]
		public string BuildConfiguration = "Release";

		[Tooltip("Target Framework (e.g., 'netstandard2.1', 'net6.0', 'net8.0', or a specific framework if multi-targeting).")]
		public string TargetFramework = "netstandard2.1";

		[Tooltip("Path to the 'dotnet' executable. If 'dotnet' is in your system's PATH, you can leave this. Otherwise, provide the full path.")]
		public string DotnetExecutablePath = "dotnet";

		[Tooltip("If true, 'dotnet restore' will be skipped during the build process. Only enable if dependencies are already restored.")]
		public bool SkipDotNetRestore = false;

		[Tooltip("If true, 'dotnet clean' will be executed before the build.")]
		public bool PerformCleanBeforeBuild = false;

		/// <summary>
		/// Gets the absolute path to the .csproj file.
		/// </summary>
		public string GetAbsoluteCsprojPath()
		{
			string unityProjectRoot = Path.GetFullPath(Application.dataPath + "/../");
			return Path.GetFullPath(Path.Combine(unityProjectRoot, PathToClassLibraryCsproj));
		}

		/// <summary>
		/// Gets the the absolute custom output directory. This is only relevant if UseDefaultOutputPath is false.
		/// Returns null if UseDefaultOutputPath is true or OutputDirectory is null/empty.
		/// </summary>
		public string GetAbsoluteOutputDirectory()
		{
			if (UseDefaultOutputPath || string.IsNullOrEmpty(OutputDirectory))
			{
				return null; // Signal to the builder to use the default dotnet output path
			}
			else
			{
				return Path.GetFullPath(Path.Combine(Application.dataPath, OutputDirectory));
			}
		}
	}
}