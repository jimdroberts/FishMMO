using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Registers the weather's render features with the core's module passes host (<see cref="ModuleRenderFeatures"/>, Phase 4 of
	/// the core and modules plan, 2026-11-18): the clouds, the height fog, the froxel fog and the heat shimmer, in the order they
	/// stood on the renderers. Each host draws its own copy of the profile's settings (<see cref="WeatherRenderProfile"/>).
	/// </summary>
	public static class WeatherRenderFeatures
	{
		public const string CloudsId = "world-simulation.clouds";
		public const string HeightFogId = "world-simulation.height-fog";
		public const string VolumetricFogId = "world-simulation.volumetric-fog";
		public const string HeatShimmerId = "world-simulation.heat-shimmer";

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		public static void Install()
		{
			ModuleRenderFeatures.Register(CloudsId, 100, () => ModuleRenderFeature.Instantiate(Profile()?.CloudsFeature));
			ModuleRenderFeatures.Register(HeightFogId, 110, () => ModuleRenderFeature.Instantiate(Profile()?.HeightFog));
			ModuleRenderFeatures.Register(VolumetricFogId, 120, () => ModuleRenderFeature.Instantiate(Profile()?.VolumetricFog));
			ModuleRenderFeatures.Register(HeatShimmerId, 130, () => ModuleRenderFeature.Instantiate(Profile()?.HeatShimmer));
		}

#if UNITY_EDITOR
		[UnityEditor.InitializeOnLoadMethod]
		private static void InstallInEditor() => Install();
#endif

		private static WeatherRenderProfile Profile()
		{
			WeatherRenderProfile profile = WeatherRenderProfile.Active;
			return profile != null ? profile : null;
		}
	}
}
