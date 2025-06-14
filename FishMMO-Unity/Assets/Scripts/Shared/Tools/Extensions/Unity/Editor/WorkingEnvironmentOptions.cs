﻿using System.IO;
using UnityEditor;

namespace FishMMO.Shared
{
	public enum WorkingEnvironmentState
	{
		Development = 0,
		Release,
	}

	[InitializeOnLoad]
	public class WorkingEnvironmentOptions
	{
		[MenuItem("FishMMO/Build/Environment/Enable Local Directory")]
		static void FishMMOUseLocalObjectsToggle()
		{
			EditorPrefs.SetBool("FishMMOEnableLocalDirectory", !EditorPrefs.GetBool("FishMMOEnableLocalDirectory"));
			Menu.SetChecked("FishMMO/Build/Environment/Enable Local Directory", EditorPrefs.GetBool("FishMMOEnableLocalDirectory"));
		}

		[MenuItem("FishMMO/Build/Environment/Release")]
		static void WorkingEnvironmentToggleOption0()
		{
			EditorPrefs.SetInt("FishMMOWorkingEnvironmentToggle", (int)WorkingEnvironmentState.Release);
		}

		[MenuItem("FishMMO/Build/Environment/Development")]
		static void WorkingEnvironmentToggleOption1()
		{
			EditorPrefs.SetInt("FishMMOWorkingEnvironmentToggle", (int)WorkingEnvironmentState.Development);
		}

		[MenuItem("FishMMO/Build/Environment/Release", true)]
		static bool WorkingEnvironmentValidation()
		{
			//Here, we uncheck all options before we show them
			Menu.SetChecked("FishMMO/Build/Environment/Development", false);
			Menu.SetChecked("FishMMO/Build/Environment/Release", false);

			WorkingEnvironmentState status = (WorkingEnvironmentState)EditorPrefs.GetInt("FishMMOWorkingEnvironmentToggle");

			//Here, we put the checkmark on the current value of WorkingEnvironmentToggle
			switch (status)
			{
				case WorkingEnvironmentState.Development:
					Menu.SetChecked("FishMMO/Build/Environment/Development", true);
					break;
				case WorkingEnvironmentState.Release:
					Menu.SetChecked("FishMMO/Build/Environment/Release", true);
					break;
			}
			return true;
		}

		public static WorkingEnvironmentState GetWorkingEnvironmentState()
		{
			return (WorkingEnvironmentState)EditorPrefs.GetInt("FishMMOWorkingEnvironmentToggle");
		}

		public static string AppendEnvironmentToPath(string path)
		{
			WorkingEnvironmentState envState = (WorkingEnvironmentState)EditorPrefs.GetInt("FishMMOWorkingEnvironmentToggle");
			switch (envState)
			{
				case WorkingEnvironmentState.Release:
					return Path.Combine(path, "Release");
				case WorkingEnvironmentState.Development:
					return Path.Combine(path, "Development");
				default:
					return path;
			}
		}
	}
}