using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>
	/// The moss layer's globals (FishMoss.hlsl), drawn with the weather as one of its overlays (<see cref="WeatherOverlays"/>,
	/// Phase 4 of the core and modules plan, 2026-11-18; the weather's presentation set them itself before): how damp the biome
	/// under the camera keeps its rock and timber (<see cref="MossClimate"/>), eased so an ecotone fades it rather than
	/// switching it, and which way is poleward (the shaded side of a wall). With no Terrain module the globals stay at their
	/// zero, and the core surface shaders grow no moss.
	/// </summary>
	public sealed class MossOverlay : IWeatherOverlay
	{
		// The scene's dampness as shown; NaN until the first frame (and after a scene change), which lands at once.
		private float dampness = float.NaN;
		private int lastFrame = -1;
		private WorldSceneSettings lastSettings;

		public void Render(Camera camera, WeatherTierSettings tier, in WeatherFrame shown, bool hasContext, in WeatherContext context, SkyOcclusionMap occlusion)
		{
			// Every camera the weather draws for asks; the scene's dampness eases once a frame.
			if (Time.frameCount == lastFrame)
			{
				return;
			}
			float dt = lastFrame < 0 ? 0f : Time.deltaTime;
			lastFrame = Time.frameCount;
			WorldSceneSettings settings = hasContext ? context.Settings : null;
			if (!ReferenceEquals(settings, lastSettings))
			{
				// A new scene's biome lands at once rather than easing over from the last one's.
				dampness = float.NaN;
				lastSettings = settings;
			}
			float target = hasContext ? MossClimate.Dampness(context.Sample.Biome) : MossClimate.Neutral;
			dampness = MossClimate.Ease(dampness, target, dt);
			MossShaderGlobals.Apply(TerrainRenderProfile.Active, dampness, settings != null ? settings.Latitude() : 0f);
		}

		public void Dispose()
		{
			MossShaderGlobals.Clear();
		}
	}
}
