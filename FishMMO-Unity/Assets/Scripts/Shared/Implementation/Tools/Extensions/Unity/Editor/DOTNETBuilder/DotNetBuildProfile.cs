using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared.DotNetBuilder
{
	/// <summary>
	/// ScriptableObject representing a build profile for .NET projects in Unity.
	/// Holds a list of build settings and accumulates build log output.
	/// </summary>
	[CreateAssetMenu(fileName = "New DotNet Build Profile", menuName = "FishMMO/Build Tools/DotNet Build Profile")]
	public class DotNetBuildProfile : ScriptableObject
	{
		/// <summary>
		/// List of .NET project build settings to process in this profile.
		/// Each entry represents a separate build configuration.
		/// </summary>
		[Tooltip("List of .NET project build settings to process.")]
		public List<DotNetBuildSettings> SettingsList = new List<DotNetBuildSettings>();

		/// <summary>
		/// Accumulates log output from build operations. Used for display in the editor and diagnostics.
		/// </summary>
		[HideInInspector]
		public string LogOutput = "";
	}
}