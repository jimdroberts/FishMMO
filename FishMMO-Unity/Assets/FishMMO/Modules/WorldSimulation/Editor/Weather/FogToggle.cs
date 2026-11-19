using FishMMO.Shared;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Switches all fog off and on in the editor, in edit mode and play mode alike (SkySystem.DrawFog): the weather's
	/// fog layer, the ground mist and the distance fog. Kept in EditorPrefs, so it holds across domain reloads and
	/// editor sessions. The twin of <see cref="CloudToggle"/>.
	/// </summary>
	[InitializeOnLoad]
	public static class FogToggle
	{
		private const string OffKey = "FishMMO.Weather.FogOff";

		static FogToggle()
		{
			SkySystem.EditorFogOff = EditorPrefs.GetBool(OffKey, false);
		}

		[DashboardTool(DashboardToolAttribute.Weather, "Toggle fog", Section = "Fog", Order = 0, AllowInPlayMode = true,
			Tooltip = "Turns all fog off or on in the Scene and Game views, in edit and play mode: the weather's fog layer (in the cloud march, the height fog and the froxel volume), the ground mist, and the distance fog, the region's and the weather's haze alike. The clouds and the waterfalls' mist stay. Remembered for this machine. Nothing is saved to any scene or asset.")]
		public static void Toggle()
		{
			bool off = !SkySystem.EditorFogOff;
			SkySystem.EditorFogOff = off;
			EditorPrefs.SetBool(OffKey, off);
			SceneView.RepaintAll();
			UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
			Debug.Log(off
				? "[Fog] Off in the editor (Dashboard → Weather Tools → Toggle fog turns it back on)."
				: "[Fog] On in the editor.");
		}
	}
}
