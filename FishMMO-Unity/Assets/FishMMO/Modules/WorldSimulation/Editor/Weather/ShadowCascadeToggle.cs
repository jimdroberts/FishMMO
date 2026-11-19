using FishMMO.Shared;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FishMMO.Client
{
	/// <summary>
	/// Play-mode A/B of the main light's shadow cascades: the active pipeline asset's count between its own and 2. The asset
	/// is a project file, so the count it had is put back as play ends (and on the next press).
	/// </summary>
	[InitializeOnLoad]
	public static class ShadowCascadeToggle
	{
		private const string SavedKey = "FishMMO.ShadowCascadeToggle.Saved";

		static ShadowCascadeToggle()
		{
			EditorApplication.playModeStateChanged -= OnPlayMode;
			EditorApplication.playModeStateChanged += OnPlayMode;
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Toggle 2 / project shadow cascades (play mode A/B)", Section = "Diagnostics", Order = 118, AllowInPlayMode = true,
			Tooltip = "Switches the main light between the pipeline asset's own shadow cascades and 2, for this play session; the asset's own count comes back when play ends.")]
		public static void Toggle()
		{
			var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
			if (asset == null || !EditorApplication.isPlaying)
			{
				Debug.Log("[Shadow cascades] Enter play mode first (the A/B is undone when play ends).");
				return;
			}
			int saved = SessionState.GetInt(SavedKey, -1);
			if (saved < 0)
			{
				SessionState.SetInt(SavedKey, asset.shadowCascadeCount);
				asset.shadowCascadeCount = 2;
				Debug.Log($"[Shadow cascades] 2 (the asset's own: {SessionState.GetInt(SavedKey, -1)}); press again to go back.");
			}
			else
			{
				Restore(asset);
			}
		}

		private static void OnPlayMode(PlayModeStateChange change)
		{
			if (change == PlayModeStateChange.ExitingPlayMode || change == PlayModeStateChange.EnteredEditMode)
			{
				Restore(GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset);
			}
		}

		private static void Restore(UniversalRenderPipelineAsset asset)
		{
			int saved = SessionState.GetInt(SavedKey, -1);
			if (saved < 0)
			{
				return;
			}
			SessionState.EraseInt(SavedKey);
			if (asset != null)
			{
				asset.shadowCascadeCount = saved;
				Debug.Log($"[Shadow cascades] Back to the asset's own {saved}.");
			}
		}
	}
}
