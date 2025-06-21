using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New DotNet Build Profile", menuName = "FishMMO/Build Tools/DotNet Build Profile")]
	public class DotNetBuildProfile : ScriptableObject
	{
		[Tooltip("List of .NET project build settings to process.")]
		public List<DotNetBuildSettings> SettingsList = new List<DotNetBuildSettings>();

		[HideInInspector]
		public string LogOutput = "";
	}
}