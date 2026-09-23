using System.Collections.Generic;
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

		/// <summary>
		/// This look as the substance makes it: the kind still decides how it falls, the substance
		/// what it is.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A copy, never an edit in place. The looks live on the render profile, which is a shared
		/// asset — tinting one would tint water snow everywhere the moment a nitrogen flurry blew
		/// through, and it would stay tinted.
		/// </para>
		/// <para>
		/// The substance SCALES rather than replaces the motion: nitrogen snow falling in thin air
		/// is faster than water snow in thick air, but it is still a flake drifting and not a
		/// raindrop. Keeping the kind's numbers as the base is what stops a substance quietly
		/// turning one kind of weather into another.
		/// </para>
		/// </remarks>
		public PrecipitationLook As(WeatherSubstance substance)
		{
			if (substance == null)
			{
				return this;
			}
			return new PrecipitationLook
			{
				AtlasRow = AtlasRow,
				Size = Size,
				Stretch = Mathf.Max(1f, Stretch * Mathf.Max(0f, substance.StretchScale)),
				FallSpeed = FallSpeed * Mathf.Max(0.01f, substance.FallSpeedScale),
				Sway = Sway,
				SwayFrequency = SwayFrequency,
				WindResponse = Mathf.Clamp(WindResponse * Mathf.Max(0f, substance.WindResponseScale), 0f, 2f),
				// The substance's own colour outright: this is the one thing it IS rather than scales.
				Tint = substance.Tint,
				Alpha = Alpha,
				// Something that glows lights itself; the rest keep the kind's brightness.
				Brightness = Brightness + substance.Emission * 2f,
				FogColor = substance.FogColor,
			};
		}

		public static PrecipitationLook Rain() => new PrecipitationLook { AtlasRow = 0, Size = new Vector2(0.018f, 0.045f), Stretch = 34f, FallSpeed = new Vector2(7f, 11f), WindResponse = 0.35f, Tint = new Color(0.78f, 0.83f, 0.92f, 1f), Alpha = 0.7f, FogColor = new Color(0.55f, 0.6f, 0.66f, 1f) };
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
		[Tooltip("Cloud shadows on the ground: the volume marched from the sun into a cookie.")]
		public bool CloudShadows = true;
		[Header("Volumetric clouds")]
		[Tooltip("Share of the screen the clouds are marched at, before the upscale.")]
		[Range(0.15f, 1f)] public float CloudResolution = 0.5f;
		[Tooltip("Steps through the cloud layer along a view ray.")]
		[Range(8, 160)] public int CloudSteps = 64;
		[Tooltip("How much of the fine detail noise is used; 0 leaves the shapes smooth.")]
		[Range(0f, 1f)] public float CloudDetail = 1f;
		[Tooltip("Steady the clouds against the last frame. Off means more steps are needed for the same calm.")]
		public bool CloudTemporal = true;
		[Tooltip("Light shafts through the clouds, and around a body during an eclipse.")]
		public bool GodRays = true;

		[Tooltip("Deep snow lifts the terrain it lies on. High Fidelity only (Q10).")]
		public bool TerrainSnowDisplacement = false;

		public static WeatherTierSettings Performant() => new WeatherTierSettings { Particles = 3000, BoxSize = 18f, OcclusionResolution = 48, OcclusionTexelMeters = 2f, OcclusionRaysPerFrame = 256, StarCubemapSize = 256, MeteorBudget = 16, Asteroids = false, ReflectionResolution = 64, Curtains = 3, CloudShadows = false, CloudResolution = 0.25f, CloudSteps = 28, CloudDetail = 0f, CloudTemporal = true, GodRays = false, TerrainSnowDisplacement = false };
		public static WeatherTierSettings Balanced() => new WeatherTierSettings { Particles = 8000, BoxSize = 24f, OcclusionResolution = 64, OcclusionTexelMeters = 1.5f, OcclusionRaysPerFrame = 512, StarCubemapSize = 512, MeteorBudget = 64, Asteroids = true, ReflectionResolution = 128, Curtains = 6, CloudShadows = true, CloudResolution = 0.4f, CloudSteps = 48, CloudDetail = 0.6f, CloudTemporal = true, GodRays = true, TerrainSnowDisplacement = false };
		public static WeatherTierSettings High() => new WeatherTierSettings { Particles = 16000, BoxSize = 30f, OcclusionResolution = 96, OcclusionTexelMeters = 1f, OcclusionRaysPerFrame = 1024, StarCubemapSize = 1024, MeteorBudget = 256, Asteroids = true, ReflectionResolution = 256, Curtains = 8, CloudShadows = true, CloudResolution = 0.5f, CloudSteps = 72, CloudDetail = 1f, CloudTemporal = true, GodRays = true, TerrainSnowDisplacement = true };
	}

	/// <summary>
	/// Where the cloud layer sits and what it is made of. The weather says how much cloud there is
	/// and what kind; this says how a cloud is built.
	/// </summary>
	[System.Serializable]
	public class VolumetricCloudSettings
	{
		[Tooltip("The DEFAULT bands of sky the clouds live in — the home world's. A body with a Cloud Stack of its own uses that instead. Lowest first. Each is a slice of atmosphere filled with 3D noise: sea level to the cloud base, the deck above it, and whatever is stacked over that.")]
		public List<CloudLayer> Layers = CloudLayerDefaults.Sky();
		[Tooltip("Planet radius used to curve the bands down to the horizon, in kilometres — when there is no body to ask. A scene stands on a body of the solar system and the body's own radius is used; this is what a scene without one falls back to.")]
		[Min(10f)] public float CurvatureRadiusKm = 6371f;
		[Tooltip("Where the noise is cut when the forecast says no cloud. The field runs about 0.33 to 0.76, so these live in that window; outside it the sky is all or nothing. Recalibrated when the bands gained vertical structure: the cut is on the noise TIMES the height profile, so giving a column real variation with height lowered the product and the old cut let far less cloud through.")]
		[Range(0.2f, 0.9f)] public float CoverageCutClear = 0.662f;
		[Tooltip("Where the noise is cut under a full overcast.")]
		[Range(0.1f, 0.8f)] public float CoverageCutFull = 0.575f;
		[Tooltip("How much further the cut drops over the last of the range, which is what closes the final gaps into an overcast.")]
		[Range(0f, 0.4f)] public float CoverageBend = 0.15f;
		[Tooltip("How soft a cloud's edge is: the width of the band where the noise thins to nothing. Clouds are fog, so this wants to be generous.")]
		[Range(0.01f, 0.4f)] public float EdgeSoftness = 0.14f;
		[Tooltip("Metres over which distance turns a cloud into haze. Smaller means the far sky greys out sooner.")]
		[Min(2000f)] public float HazeDistance = 28000f;
		[Tooltip("How far the shape lookup is bent to stop the noise repeating. The shape volume wraps, so without this a band tiles visibly; too much and the warp field's own structure is stamped onto the clouds as combed, hairy edges, because the lookup then moves faster from the warp than it does from going anywhere. Around 0.17 keeps the warp to about a quarter of the lookup's own motion. Zero turns it off and brings the tiling back.")]
		[Range(0f, 0.5f)] public float ShapeWarp = 0.17f;
		[Tooltip("How much the sky is allowed to tilt toward the weather that is coming. A front is a slope in cloud rather than a level of it, and this is what lets cloud arrive from upwind instead of appearing everywhere at once. Zero turns it off and the whole sky takes one cover again. It only applies where the drifting weather field is what decides the weather.")]
		[Range(0f, 1f)] public float CoverageTilt = 0.6f;
		[Tooltip("Metres out to which the fine detail noise is used in full. The detail tiles every few hundred metres and the march steps tens of metres, so past a point it is sampled far too coarsely and turns into speckle.")]
		[Min(0f)] public float DetailFadeStart = 2500f;
		[Tooltip("Metres over which that detail fades away entirely. Beyond it a cloud is its shape alone, which is what distance does to one anyway.")]
		[Min(100f)] public float DetailFadeRange = 9000f;

		[Tooltip("How hard the cauliflower is carved into a cloud: a second, finer read of the shape volume that eats the body back to its billows, harder toward the top of a column where a real cloud is most broken up. 0 leaves smooth masses; 1 is the shipped look; above that the cloud starts to come apart.")]
		[Range(0f, 2f)] public float Carve = 1f;
		[Tooltip("Optical density: higher is thicker and darker inside. 1 is about three hundredths a metre of extinction, which is real cloud: a 300 m heap is opaque, a 60 m wisp lets a fifth of the light through, and an edge fades over the tens of metres it has. It used to be read as whole units a metre, which made every edge solid within a single step and the sky a thresholded noise field.")]
		[Range(0.05f, 4f)] public float Density = 0.5f;
		[Tooltip("Steps toward the sun when lighting a point in the cloud.")]
		[Range(1, 12)] public int LightSteps = 6;
		[Tooltip("The dark edge a sunlit cloud shows before it brightens (the powder effect).")]
		[Range(0f, 1f)] public float Powder = 0.7f;
		[Tooltip("How much light keeps going forward: the glow around the sun through thin cloud.")]
		[Range(0f, 0.95f)] public float ForwardScatter = 0.6f;
		[Tooltip("How much of the sky's own light fills the shaded side.")]
		[Range(0f, 4f)] public float Ambient = 0.85f;
		[Tooltip("The colour a shaded underside takes. Slate-blue reads as cloud; white reads as fog.")]
		[ColorUsage(false, false)] public Color ShadedTint = new Color(0.62f, 0.68f, 0.82f);
		[Tooltip("How much of that tint a fair-weather underside takes. A storm takes more.")]
		[Range(0f, 1f)] public float ShadedTintStrength = 0.55f;
		[Tooltip("How far the clouds are drawn, in metres. They dissolve over the last quarter of it, and the haze is full by four fifths of it, so the sky ends in the colour of the horizon and not on an edge. It was 90 km: haze had turned a cloud flat by 20, so the other 70 were marched at full cost — along the longest rays in the frame — to draw a smear. A deck a kilometre up is three degrees above the horizon at 20 km; raise this only if a distant tower has to stand on the skyline.")]
		[Min(1000f)] public float MaxDistance = 44000f;
		[Tooltip("How much the last frame is kept when the clouds are steadied.")]
		[Range(0f, 0.98f)] public float TemporalBlend = 0.9f;
		[Tooltip("How dark the ground goes under a cloud.")]
		[Range(0f, 1f)] public float ShadowStrength = 0.8f;
		[Tooltip("Metres across that the cloud shadow cookie covers.")]
		[Min(200f)] public float ShadowAreaMeters = 4000f;
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
		public Material CloudMaterial;
		public Material SkyBodyMaterial;
		public Material CurtainMaterial;
		public Material BoltMaterial;
		public Material CloudCookieMaterial;

		[Header("Volumetric clouds")]
		[Tooltip("The shape and detail volumes the clouds are carved from (Weather Tools → Bake cloud noise).")]
		public Texture3D CloudShape;
		public Texture3D CloudDetail;
		public VolumetricCloudSettings Clouds = new VolumetricCloudSettings();

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
