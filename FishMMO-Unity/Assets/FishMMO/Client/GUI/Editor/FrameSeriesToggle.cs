using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Switches the frame series (<see cref="FrameSeries"/>) on and off. Kept in EditorPrefs, so it holds across domain
	/// reloads and editor sessions on this machine; off by default.
	/// </summary>
	public static class FrameSeriesToggle
	{
		[DashboardTool(DashboardToolAttribute.Maintenance, "Toggle frame series", Section = "Diagnostics", Order = 1, AllowInPlayMode = true,
			Tooltip = "Records, on every Play, a row a second for the first three minutes and a row every 15 s after: frame, GPU and CPU times, memory, GC runs, the grass and scatter take-on, and the systems that cost the most. Written to the Console and Editor.log as [FrameSeries]. Off by default; remembered for this machine.")]
		public static void Toggle()
		{
			bool on = !FrameSeries.Enabled;
			FrameSeries.Enabled = on;
			Debug.Log(on
				? "[FrameSeries] On: the next Play records the series (Dashboard → Maintenance → Toggle frame series turns it off)."
				: "[FrameSeries] Off.");
		}
	}
}
