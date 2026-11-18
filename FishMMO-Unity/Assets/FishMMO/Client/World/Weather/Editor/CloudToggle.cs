using FishMMO.Shared;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Switches the volumetric clouds off and on in the editor, in edit mode and play mode alike (SkySystem.DrawClouds).
	/// Kept in EditorPrefs, so it holds across domain reloads and editor sessions; the player's own switch is the
	/// options' Volumetric Clouds toggle (ClientCloudSettings), and the clouds draw only while both are on.
	/// </summary>
	[InitializeOnLoad]
	public static class CloudToggle
	{
		private const string OffKey = "FishMMO.Weather.CloudsOff";

		static CloudToggle()
		{
			SkySystem.EditorCloudsOff = EditorPrefs.GetBool(OffKey, false);
		}

		[DashboardTool(DashboardToolAttribute.Weather, "Toggle volumetric clouds", Section = "Clouds", Order = 0, AllowInPlayMode = true,
			Tooltip = "Turns the volumetric clouds off or on in the Scene and Game views, in edit and play mode: the march, its ground shadows, god rays and light volume. Remembered for this machine. Nothing is saved to any scene or asset.")]
		public static void Toggle()
		{
			bool off = !SkySystem.EditorCloudsOff;
			SkySystem.EditorCloudsOff = off;
			EditorPrefs.SetBool(OffKey, off);
			SceneView.RepaintAll();
			UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
			Debug.Log(off
				? "[Clouds] Off in the editor (Dashboard → Weather Tools → Toggle volumetric clouds turns them back on)."
				: ClientCloudSettings.Enabled
					? "[Clouds] On in the editor."
					: "[Clouds] On in the editor, but the player's Volumetric Clouds option is off, so they stay hidden.");
		}
	}
}
