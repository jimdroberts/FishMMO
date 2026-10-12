using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Client
{
	/// <summary>
	/// The moss layer (FishMoss.hlsl) out of play mode. Its globals are written by <see cref="WeatherPresentation"/>,
	/// which only runs in play; without this every mossy rock and wall would show bare in the Scene view while it was
	/// being placed. Publishes the profile's strength at its editor dampness (WeatherRenderProfile.MossEditorDampness)
	/// before each render, and stands aside in play mode.
	/// </summary>
	[InitializeOnLoad]
	internal static class MossEditorPreview
	{
		static MossEditorPreview()
		{
			RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
			RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
		}

		private static void OnBeginContextRendering(ScriptableRenderContext context, System.Collections.Generic.List<Camera> cameras)
		{
			if (EditorApplication.isPlayingOrWillChangePlaymode)
			{
				return;
			}
			WeatherRenderProfile profile = WeatherRenderProfile.Active;
			if (profile == null)
			{
				return;
			}
			// No scene's latitude here: the northern hemisphere's poleward side, as most of the world is authored.
			MossShaderGlobals.Apply(profile, profile.MossEditorDampness, 45f);
		}
	}
}
