using System.IO;
using UnityEditor;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents the working environment state for builds (Development or Production).
	/// </summary>
	public enum WorkingEnvironmentState
	{
		/// <summary>Development environment.</summary>
		Development = 0,
		/// <summary>Production environment.</summary>
		Production,
	}

	/// <summary>
	/// Provides menu options and utilities for managing the working environment state in FishMMO builds.
	/// </summary>
	[InitializeOnLoad]
	public class WorkingEnvironmentOptions
	{
		/// <summary>
		/// Toggles the use of the local directory for assets and updates the menu checkmark.
		/// </summary>
		[MenuItem("FishMMO/Build/Environment/Enable Local Directory")]
		static void FishMMOUseLocalObjectsToggle()
		{
			EditorPrefs.SetBool("FishMMOEnableLocalDirectory", !EditorPrefs.GetBool("FishMMOEnableLocalDirectory"));
			Menu.SetChecked("FishMMO/Build/Environment/Enable Local Directory", EditorPrefs.GetBool("FishMMOEnableLocalDirectory"));
		}

		/// <summary>
		/// Sets the working environment to Production mode.
		/// </summary>
		[MenuItem("FishMMO/Build/Environment/Production")]
		static void WorkingEnvironmentToggleOption0()
		{
			EditorPrefs.SetInt("FishMMOWorkingEnvironmentToggle", (int)WorkingEnvironmentState.Production);
		}

		/// <summary>
		/// Sets the working environment to Development mode.
		/// </summary>
		[MenuItem("FishMMO/Build/Environment/Development")]
		static void WorkingEnvironmentToggleOption1()
		{
			EditorPrefs.SetInt("FishMMOWorkingEnvironmentToggle", (int)WorkingEnvironmentState.Development);
		}

		/// <summary>
		/// Validation method for environment menu. Updates checkmarks based on current environment state.
		/// </summary>
		/// <returns>True if menu should be shown.</returns>
		[MenuItem("FishMMO/Build/Environment/Production", true)]
		static bool WorkingEnvironmentValidation()
		{
			// Uncheck all options before showing them
			Menu.SetChecked("FishMMO/Build/Environment/Development", false);
			Menu.SetChecked("FishMMO/Build/Environment/Production", false);

			WorkingEnvironmentState status = (WorkingEnvironmentState)EditorPrefs.GetInt("FishMMOWorkingEnvironmentToggle");

			// Put the checkmark on the current value of WorkingEnvironmentToggle
			switch (status)
			{
				case WorkingEnvironmentState.Development:
					Menu.SetChecked("FishMMO/Build/Environment/Development", true);
					break;
				case WorkingEnvironmentState.Production:
					Menu.SetChecked("FishMMO/Build/Environment/Production", true);
					break;
			}
			return true;
		}

		/// <summary>
		/// Gets the current working environment state (Development or Production).
		/// </summary>
		/// <returns>The current WorkingEnvironmentState.</returns>
		public static WorkingEnvironmentState GetWorkingEnvironmentState()
		{
			return (WorkingEnvironmentState)EditorPrefs.GetInt("FishMMOWorkingEnvironmentToggle");
		}

		/// <summary>
		/// Appends the current environment (Development or Production) to the given path.
		/// </summary>
		/// <param name="path">The base path to append the environment folder to.</param>
		/// <returns>The path with the environment folder appended.</returns>
		public static string AppendEnvironmentToPath(string path)
		{
			WorkingEnvironmentState envState = (WorkingEnvironmentState)EditorPrefs.GetInt("FishMMOWorkingEnvironmentToggle");
			switch (envState)
			{
				case WorkingEnvironmentState.Production:
					return Path.Combine(path, "Production");
				case WorkingEnvironmentState.Development:
					return Path.Combine(path, "Development");
				default:
					return path;
			}
		}
	}
}