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
	}
}