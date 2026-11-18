using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Switches the start-up report (<see cref="StartupTimeline"/>) on and off. Kept in EditorPrefs, so it holds across
	/// domain reloads and editor sessions on this machine; off by default.
	/// </summary>
	public static class StartupTimelineToggle
	{
		[DashboardTool(DashboardToolAttribute.Maintenance, "Toggle start-up timeline", Section = "Diagnostics", Order = 0, AllowInPlayMode = true,
			Tooltip = "Records, on every Play, how long the freeze before the first frame lasted, when each scene loaded, how long shaders compiled and every hitch with what spent it, then writes one [StartupTimeline] report to the Console and Editor.log. Off by default; remembered for this machine.")]
		public static void Toggle()
		{
			bool on = !StartupTimeline.Enabled;
			StartupTimeline.Enabled = on;
			Debug.Log(on
				? "[StartupTimeline] On: the next Play writes a start-up report (Dashboard → Maintenance → Toggle start-up timeline turns it off)."
				: "[StartupTimeline] Off.");
		}
	}
}
