using UnityEditor;

// Originally from: https://support.unity.com/hc/en-us/articles/210452343-How-to-stop-automatic-assembly-compilation-from-script
// Allowing you to toggle the various script compilation options quickly and easily
// Since Unity can't work properly on a recompile...

namespace FishMMO.Shared
{
	[InitializeOnLoad]
	public class ScriptOptions
	{
		static ScriptOptions()
		{
			EditorApplication.playModeStateChanged += PlaymodeChanged;
		}

		// Script Compilation During Play

		// ScriptCompilationDuringPlay has three posible values
		// 0 = Recompile And Continue Playing
		// 1 = Recompile After Finished Playing
		// 2 = Stop Playing And Recompile

		// The following methods assing the three possible values to ScriptCompilationDuringPlay
		// depending on the option you selected
		/// <summary>
		/// Sets script compilation mode to 'Recompile And Continue Playing'.
		/// </summary>
		[MenuItem("FishMMO/Script Compilation/Recompile And Continue Playing")]
		static void ScriptCompilationToggleOption0()
		{
			EditorPrefs.SetInt("ScriptCompilationDuringPlay", 0);
		}

		/// <summary>
		/// Sets script compilation mode to 'Recompile After Finished Playing'.
		/// </summary>
		[MenuItem("FishMMO/Script Compilation/Recompile After Finished Playing")]
		static void ScriptCompilationToggleOption1()
		{
			EditorPrefs.SetInt("ScriptCompilationDuringPlay", 1);
		}

		/// <summary>
		/// Sets script compilation mode to 'Stop Playing And Recompile'.
		/// </summary>
		[MenuItem("FishMMO/Script Compilation/Stop Playing And Recompile")]
		static void ScriptCompilationToggleOption2()
		{
			EditorPrefs.SetInt("ScriptCompilationDuringPlay", 2);
		}

		/// <summary>
		/// Validation method for script compilation menu. Updates checkmarks based on current mode.
		/// </summary>
		/// <returns>True if menu should be shown.</returns>
		[MenuItem("FishMMO/Script Compilation/Recompile And Continue Playing", true)]
		static bool ScriptCompilationValidation()
		{
			// Uncheck all options before showing them
			Menu.SetChecked("FishMMO/Script Compilation/Recompile And Continue Playing", false);
			Menu.SetChecked("FishMMO/Script Compilation/Recompile After Finished Playing", false);
			Menu.SetChecked("FishMMO/Script Compilation/Stop Playing And Recompile", false);

			var status = EditorPrefs.GetInt("ScriptCompilationDuringPlay");

			// Put the checkmark on the current value of ScriptCompilationDuringPlay
			switch (status)
			{
				case 0:
					Menu.SetChecked("FishMMO/Script Compilation/Recompile And Continue Playing", true);
					break;
				case 1:
					Menu.SetChecked("FishMMO/Script Compilation/Recompile After Finished Playing", true);
					break;
				case 2:
					Menu.SetChecked("FishMMO/Script Compilation/Stop Playing And Recompile", true);
					break;
			}
			return true;
		}

		/// <summary>
		/// Handles playmode change events to control assembly reload behavior based on script compilation mode.
		/// </summary>
		/// <param name="state">The current play mode state change.</param>
		static void PlaymodeChanged(PlayModeStateChange state)
		{
			// Enable assembly reload when leaving play mode/entering edit mode
			if (state == PlayModeStateChange.ExitingPlayMode
				|| state == PlayModeStateChange.EnteredEditMode)
			{
				EditorApplication.UnlockReloadAssemblies();
			}

			// If we should recompile after finishing playing
			// Multiplayer does not like reloading assemblies during play
			if (EditorPrefs.GetInt("ScriptCompilationDuringPlay") != 1) return;

			// Disable assembly reload when leaving edit mode/entering play mode
			if (state == PlayModeStateChange.EnteredPlayMode
				|| state == PlayModeStateChange.ExitingEditMode)
			{
				EditorApplication.LockReloadAssemblies();
			}
		}

		//Auto Refresh

		//kAutoRefresh has two posible values
		//0 = Auto Refresh Disabled
		//1 = Auto Refresh Enabled

		//This is called when you click on the 'Tools/Auto Refresh' and toggles its value
		/// <summary>
		/// Toggles the auto-refresh setting in the Unity Editor.
		/// </summary>
		[MenuItem("FishMMO/Script Compilation/Auto Refresh")]
		static void AutoRefreshToggle()
		{
			var status = EditorPrefs.GetInt("kAutoRefresh");
			if (status == 1)
				EditorPrefs.SetInt("kAutoRefresh", 0);
			else
				EditorPrefs.SetInt("kAutoRefresh", 1);
		}

		//This is called before 'Tools/Auto Refresh' is shown to check the current value
		//of kAutoRefresh and update the checkmark
		/// <summary>
		/// Validation method for auto-refresh menu. Updates checkmark based on current setting.
		/// </summary>
		/// <returns>True if menu should be shown.</returns>
		[MenuItem("FishMMO/Script Compilation/Auto Refresh", true)]
		static bool AutoRefreshToggleValidation()
		{
			var status = EditorPrefs.GetInt("kAutoRefresh");
			if (status == 1)
				Menu.SetChecked("FishMMO/Script Compilation/Auto Refresh", true);
			else
				Menu.SetChecked("FishMMO/Script Compilation/Auto Refresh", false);
			return true;
		}
	}
}