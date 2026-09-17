using System;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Weather;

namespace FishMMO.Client
{
	/// <summary>How one kind of precipitation looks.</summary>
	[Serializable]
	public class PrecipitationLook
	{
		[Tooltip("Atlas row: 0 streak, 1 flake, 2 hail, 3 ember/ash, 4 grit.")]
		[Range(0, 7)] public int AtlasRow;
		[Tooltip("Particle width in metres at drop size 0 and 1.")]
		public Vector2 Size = new Vector2(0.02f, 0.04f);
		[Tooltip("Length ÷ width. Above 1 draws a streak along the fall.")]
		[Min(1f)] public float Stretch = 1f;
		[Tooltip("Fall speed in m/s at drop size 0 and 1.")]
		public Vector2 FallSpeed = new Vector2(1f, 1.5f);
		[Tooltip("How far the particle sways, in metres.")]
		[Min(0f)] public float Sway;
		[Tooltip("Sway speed.")]
		[Min(0f)] public float SwayFrequency = 1f;
		[Tooltip("How much the wind pushes it: 1 for sand, less for heavy rain.")]
		[Range(0f, 2f)] public float WindResponse = 0.5f;
		public Color Tint = Color.white;
		[Range(0f, 1f)] public float Alpha = 0.6f;
		[Min(0f)] public float Brightness = 1f;
		[Tooltip("Fog colour this kind of weather pushes toward.")]
		public Color FogColor = new Color(0.6f, 0.65f, 0.7f, 1f);

		public static PrecipitationLook Rain() => new PrecipitationLook { AtlasRow = 0, Size = new Vector2(0.008f, 0.016f), Stretch = 40f, FallSpeed = new Vector2(7f, 10f), WindResponse = 0.35f, Tint = new Color(0.75f, 0.8f, 0.9f, 1f), Alpha = 0.45f, FogColor = new Color(0.55f, 0.6f, 0.66f, 1f) };
		public static PrecipitationLook Snow() => new PrecipitationLook { AtlasRow = 1, Size = new Vector2(0.03f, 0.08f), Stretch = 1f, FallSpeed = new Vector2(0.8f, 1.4f), Sway = 0.35f, SwayFrequency = 0.8f, WindResponse = 0.9f, Tint = Color.white, Alpha = 0.9f, FogColor = new Color(0.82f, 0.85f, 0.9f, 1f) };
		public static PrecipitationLook Hail() => new PrecipitationLook { AtlasRow = 2, Size = new Vector2(0.03f, 0.06f), Stretch = 1f, FallSpeed = new Vector2(10f, 16f), WindResponse = 0.2f, Tint = new Color(0.9f, 0.95f, 1f, 1f), Alpha = 0.95f, FogColor = new Color(0.6f, 0.64f, 0.7f, 1f) };
		public static PrecipitationLook Ash() => new PrecipitationLook { AtlasRow = 3, Size = new Vector2(0.08f, 0.14f), Stretch = 1f, FallSpeed = new Vector2(0.4f, 0.8f), Sway = 0.5f, SwayFrequency = 0.5f, WindResponse = 0.6f, Tint = new Color(0.55f, 0.53f, 0.5f, 1f), Alpha = 0.95f, FogColor = new Color(0.42f, 0.4f, 0.38f, 1f) };
		public static PrecipitationLook Sand() => new PrecipitationLook { AtlasRow = 4, Size = new Vector2(0.1f, 0.18f), Stretch = 3f, FallSpeed = new Vector2(0.5f, 1f), Sway = 0.2f, SwayFrequency = 2f, WindResponse = 0.6f, Tint = new Color(0.9f, 0.78f, 0.58f, 1f), Alpha = 0.9f, FogColor = new Color(0.78f, 0.66f, 0.46f, 1f) };
	}

	/// <summary>What a quality level spends on weather.</summary>
	[Serializable]
	public class WeatherTierSettings
	{
		[Tooltip("Particles in the precipitation field.")]
		[Range(500, 20000)] public int Particles = 8000;
		[Tooltip("Size of the box of particles around the camera, in metres.")]
		[Min(4f)] public float BoxSize = 24f;
		[Tooltip("Sky occlusion map resolution (texels per side).")]
		[Range(16, 128)] public int OcclusionResolution = 64;
		[Tooltip("Metres per sky occlusion texel.")]
		[Min(0.25f)] public float OcclusionTexelMeters = 1.5f;
		[Tooltip("Raycasts per frame while the occlusion map is rebuilt.")]
		[Range(16, 4096)] public int OcclusionRaysPerFrame = 512;
		[Header("Sky")]
		[Tooltip("Star field cubemap size per face.")]
		[Range(128, 2048)] public int StarCubemapSize = 512;
		[Tooltip("Meteors that can be alight at once.")]
		[Range(0, 512)] public int MeteorBudget = 64;
		[Tooltip("Draw asteroid belts as points.")]
		public bool Asteroids = true;
		[Tooltip("Sky reflection resolution; 0 turns the reflection off.")]
		[Range(0, 512)] public int ReflectionResolution = 128;
		[Tooltip("Distant rain curtains under the nearest storm cells.")]
		[Range(0, 16)] public int Curtains = 8;
		[Tooltip("Cloud shadows on the ground (a cookie on the sun).")]
		public bool CloudShadows = true;

		public static WeatherTierSettings Performant() => new WeatherTierSettings { Particles = 3000, BoxSize = 18f, OcclusionResolution = 48, OcclusionTexelMeters = 2f, OcclusionRaysPerFrame = 256, StarCubemapSize = 256, MeteorBudget = 16, Asteroids = false, ReflectionResolution = 64, Curtains = 3, CloudShadows = false };
		public static WeatherTierSettings Balanced() => new WeatherTierSettings { Particles = 8000, BoxSize = 24f, OcclusionResolution = 64, OcclusionTexelMeters = 1.5f, OcclusionRaysPerFrame = 512, StarCubemapSize = 512, MeteorBudget = 64, Asteroids = true, ReflectionResolution = 128, Curtains = 6, CloudShadows = true };
		public static WeatherTierSettings High() => new WeatherTierSettings { Particles = 16000, BoxSize = 30f, OcclusionResolution = 96, OcclusionTexelMeters = 1f, OcclusionRaysPerFrame = 1024, StarCubemapSize = 1024, MeteorBudget = 256, Asteroids = true, ReflectionResolution = 256, Curtains = 8, CloudShadows = true };
	}

	/// <summary>
	/// The client's weather look: materials, textures, per-kind looks, fog and per-tier budgets.
	/// Client-only; loaded with the client's static assets.
	/// </summary>
	[CreateAssetMenu(fileName = "Weather Render Profile", menuName = "FishMMO/Weather/Render Profile", order = 20)]
	public class WeatherRenderProfile : CachedScriptableObject<WeatherRenderProfile>, ICachedObject
	{
		[Header("Precipitation")]
		public Material PrecipitationMaterial;
		public Texture2D PrecipitationAtlas;
		public Texture2D Noise;
		public PrecipitationLook Rain = PrecipitationLook.Rain();
		public PrecipitationLook Snow = PrecipitationLook.Snow();
		public PrecipitationLook Hail = PrecipitationLook.Hail();
		public PrecipitationLook Ash = PrecipitationLook.Ash();
		public PrecipitationLook Sand = PrecipitationLook.Sand();

		[Header("Sky")]
		public Material SkyMaterial;
		public Material SkyBodyMaterial;
		public Material CurtainMaterial;
		public Material BoltMaterial;
		public Material CloudCookieMaterial;

		[Header("Fog")]
		[Tooltip("Colour of fog from the fog channel alone (mist).")]
		public Color MistColor = new Color(0.7f, 0.73f, 0.76f, 1f);
		[Tooltip("Exponential density added at fog 1.")]
		[Min(0f)] public float MaxFogDensity = 0.06f;
		[Tooltip("Linear fog end distance at fog 1, in metres.")]
		[Min(5f)] public float MinFogEndDistance = 40f;

		[Header("Budgets per quality level (Performant, Balanced, High Fidelity)")]
		public WeatherTierSettings Performant = WeatherTierSettings.Performant();
		public WeatherTierSettings Balanced = WeatherTierSettings.Balanced();
		public WeatherTierSettings HighFidelity = WeatherTierSettings.High();

		[Header("Sky occlusion")]
		[Tooltip("What counts as a roof.")]
		public LayerMask OcclusionLayers = 1 | (1 << 7);

		[Header("Audio")]
		public WeatherAudioProfile Audio;

		/// <summary>The loaded profile, if any.</summary>
		public static WeatherRenderProfile Active => GetFirst<WeatherRenderProfile>();

		/// <summary>The budget for a quality level index (0 Performant, 1 Balanced, 2+ High Fidelity).</summary>
		public WeatherTierSettings TierFor(int qualityLevel)
		{
			return qualityLevel <= 0 ? Performant : qualityLevel == 1 ? Balanced : HighFidelity;
		}

		public PrecipitationLook LookOf(WeatherChannel typeChannel)
		{
			switch (typeChannel)
			{
				case WeatherChannel.SnowWeight: return Snow;
				case WeatherChannel.HailWeight: return Hail;
				case WeatherChannel.AshWeight: return Ash;
				case WeatherChannel.SandWeight: return Sand;
				default: return Rain;
			}
		}
	}
}
