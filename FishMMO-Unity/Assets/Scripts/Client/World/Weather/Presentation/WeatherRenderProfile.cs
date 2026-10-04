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
		[Tooltip("Particle width in metres at drop size 0 and 1. Snow: the smallest and the largest flake, m — each flake's size between them is the snowfall's own, from how hard it snows and how warm the air is (SnowflakeSize).")]
		public Vector2 Size = new Vector2(0.02f, 0.04f);
		[Tooltip("Length ÷ width. Above 1 draws a streak along the fall.")]
		[Min(1f)] public float Stretch = 1f;
		[Tooltip("How fast its typical particle falls on a world like ours, in m/s, at drop size 0 and 1: its terminal speed. Snow: at a 1 mm flake, and the fastest flake — between them speed goes as the flake's size to the 0.16. Another world's gravity and air are applied on top, and the wind carries it sideways as it carries the air.")]
		public Vector2 FallSpeed = new Vector2(1f, 1.5f);
		[Tooltip("How far the particle sways, in metres.")]
		[Min(0f)] public float Sway;
		[Tooltip("Sway speed.")]
		[Min(0f)] public float SwayFrequency = 1f;
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
				// The substance's own colour outright: this is the one thing it IS rather than scales.
				Tint = substance.Tint,
				Alpha = Alpha,
				// Something that glows lights itself; the rest keep the kind's brightness.
				Brightness = Brightness + substance.Emission * 2f,
				FogColor = substance.FogColor,
			};
		}

		// Rain and hail are water and ice, which colour nothing they pass or scatter: white. The
		// shader lights them with the scene's own sky and sun, which carry the sky's hue. Rain was a
		// painted blue (0.78, 0.83, 0.92) and hail a paler one, on top of that.
		public static PrecipitationLook Rain() => new PrecipitationLook { AtlasRow = 0, Size = new Vector2(0.018f, 0.045f), Stretch = 34f, FallSpeed = new Vector2(4f, 7f), Tint = Color.white, Alpha = 0.7f, FogColor = new Color(0.55f, 0.6f, 0.66f, 1f) };
		// Snow's Size is its smallest and largest flake, as measured (SnowflakeSize): it was 3–8 cm,
		// swollen by rain's growth law to forty centimetres in a blizzard. Its FallSpeed is Locatelli and
		// Hobbs' 0.8 m/s at a millimetre, up to the 1.4 of a three-centimetre aggregate.
		public static PrecipitationLook Snow() => new PrecipitationLook { AtlasRow = 1, Size = new Vector2(SnowflakeSize.SmallestMillimetres * 0.001f, SnowflakeSize.LargestAggregateMillimetres * 0.001f), Stretch = 1f, FallSpeed = new Vector2(0.8f, 1.4f), Sway = 0.35f, SwayFrequency = 0.8f, Tint = Color.white, Alpha = 0.9f, FogColor = new Color(0.82f, 0.85f, 0.9f, 1f) };
		public static PrecipitationLook Hail() => new PrecipitationLook { AtlasRow = 2, Size = new Vector2(0.03f, 0.06f), Stretch = 1f, FallSpeed = new Vector2(10f, 16f), Tint = Color.white, Alpha = 0.95f, FogColor = new Color(0.6f, 0.64f, 0.7f, 1f) };
		// Ash and sand are grains, sized as grains. The ash sprite's blob fills about 0.44 of its
		// quad and the grit's specks 0.06–0.16, so these draw ash as 1–4 mm flakes and aggregates
		// (what an ashfall is seen as; single shards are under 2 mm) and blown sand as 1–5 mm grit.
		// They were 8–14 and 10–18 cm, a size no grain of either reaches.
		public static PrecipitationLook Ash() => new PrecipitationLook { AtlasRow = 3, Size = new Vector2(0.003f, 0.01f), Stretch = 1f, FallSpeed = new Vector2(0.4f, 0.8f), Sway = 0.5f, SwayFrequency = 0.5f, Tint = new Color(0.55f, 0.53f, 0.5f, 1f), Alpha = 0.95f, FogColor = new Color(0.42f, 0.4f, 0.38f, 1f) };
		public static PrecipitationLook Sand() => new PrecipitationLook { AtlasRow = 4, Size = new Vector2(0.012f, 0.03f), Stretch = 3f, FallSpeed = new Vector2(0.5f, 1f), Sway = 0.2f, SwayFrequency = 2f, Tint = new Color(0.9f, 0.78f, 0.58f, 1f), Alpha = 0.9f, FogColor = new Color(0.78f, 0.66f, 0.46f, 1f) };
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
		[Tooltip("Tornadoes and dust devils drawn at once, nearest first.")]
		[Range(0, 8)] public int Vortices = 3;
		[Tooltip("Debris particles whirling round a tornado; a dust devil takes half.")]
		[Range(0, 8192)] public int VortexParticles = 2000;
		[Tooltip("Cloud shadows on the ground: the volume marched from the sun into a cookie.")]
		public bool CloudShadows = true;
		[Header("Volumetric clouds")]
		[Tooltip("Share of the screen the clouds are marched at, across, before the steadying rebuilds them. The rays are the cost: a quarter of them is about a third of the march's time.")]
		[Range(0.15f, 1f)] public float CloudResolution = 0.5f;
		[Tooltip("Steps through the cloud layer along a view ray.")]
		[Range(8, 160)] public int CloudSteps = 64;
		[Tooltip("How much of the eddies' pattern is drawn at a cloud's edge, 0..1. Their average effect is kept at any setting (the edge's water, CloudClimate.ExpectedEdgeWater), so this changes how frayed an edge looks, not how much cloud there is; above 0 it costs one detail read per sample inside cloud.")]
		[Range(0f, 1f)] public float CloudDetail = 1f;
		[Tooltip("Steady the clouds against the last frame. Off means more steps are needed for the same calm.")]
		public bool CloudTemporal = true;
		/// <summary>
		/// Share of the screen the steadied clouds are rebuilt at; 0 lets the renderer choose
		/// (<see cref="CloudTierSettings.HistoryScaleFor"/>: no more than 4 pixels a marched texel
		/// across, so every pixel is marched again once in sixteen frames).
		/// </summary>
		/// <remarks>
		/// A cost, not a look: nothing about the clouds depends on it. The steadying costs the same
		/// for every pixel it rebuilds whatever the march cost, so a tier marching a sixteenth of the
		/// screen paid as much to steady it as to march it; rebuilt at less than the screen, the
		/// composite scales it the rest of the way. 0 is also what a profile saved before this field
		/// existed reads, which gives every tier of it exactly the numbers below.
		/// </remarks>
		[Tooltip("Share of the screen the steadied clouds are rebuilt at. 0: no more than 4 pixels a marched texel across (a sixteen-frame refresh) — the screen's size on Balanced and High, about two thirds on Performant. Never coarser than the march.")]
		[Range(0f, 1f)] public float CloudHistoryScale = 0f;
		[Tooltip("Light shafts through the clouds, and around a body during an eclipse.")]
		public bool GodRays = true;

		[Tooltip("Deep snow lifts the terrain it lies on. High Fidelity only (Q10).")]
		public bool TerrainSnowDisplacement = false;

		// Balanced's CloudDetail was 0.6. The erosion's average is now kept at any setting, so 0.6 only
		// drew the eddies at 60 % contrast for the same texture read: it is 1. Performant stays 0, which
		// skips the read and keeps the average (smooth-edged clouds of the same size).
		public static WeatherTierSettings Performant() => new WeatherTierSettings { Particles = 3000, BoxSize = 18f, OcclusionResolution = 48, OcclusionTexelMeters = 2f, OcclusionRaysPerFrame = 256, StarCubemapSize = 256, MeteorBudget = 16, Asteroids = false, ReflectionResolution = 64, Curtains = 3, Vortices = 2, VortexParticles = 600, CloudShadows = false, CloudResolution = 0.17f, CloudSteps = 28, CloudDetail = 0f, CloudTemporal = true, CloudHistoryScale = 0f, GodRays = false, TerrainSnowDisplacement = false };
		public static WeatherTierSettings Balanced() => new WeatherTierSettings { Particles = 8000, BoxSize = 24f, OcclusionResolution = 64, OcclusionTexelMeters = 1.5f, OcclusionRaysPerFrame = 512, StarCubemapSize = 512, MeteorBudget = 64, Asteroids = true, ReflectionResolution = 128, Curtains = 6, Vortices = 3, VortexParticles = 1500, CloudShadows = true, CloudResolution = 0.25f, CloudSteps = 48, CloudDetail = 1f, CloudTemporal = true, CloudHistoryScale = 0f, GodRays = true, TerrainSnowDisplacement = false };
		public static WeatherTierSettings High() => new WeatherTierSettings { Particles = 16000, BoxSize = 30f, OcclusionResolution = 96, OcclusionTexelMeters = 1f, OcclusionRaysPerFrame = 1024, StarCubemapSize = 1024, MeteorBudget = 256, Asteroids = true, ReflectionResolution = 256, Curtains = 8, Vortices = 4, VortexParticles = 3000, CloudShadows = true, CloudResolution = 0.3f, CloudSteps = 72, CloudDetail = 1f, CloudTemporal = true, CloudHistoryScale = 0f, GodRays = true, TerrainSnowDisplacement = true };
	}

	/// <summary>
	/// What the clouds may cost to draw. Nothing about the clouds themselves: where they sit, how
	/// much of them there is, what shape and how opaque, all come from the air (see
	/// <see cref="FishMMO.Shared.Weather.CloudClimate"/>). These are how far and how finely the
	/// renderer is allowed to look at them.
	/// </summary>
	[System.Serializable]
	public class VolumetricCloudSettings
	{
		// DetailFadeStart / DetailFadeRange (2.5 km, 9 km) are gone: the detail's octaves are each drawn
		// while the screen can resolve them and fade to their mean after (CloudClimate.DetailResolved),
		// so how far detail reaches follows the resolution rather than a fixed distance — which was
		// nearer than most of the clouds in view, and took the edges' erosion away with it.
		[Tooltip("Steps toward the sun when lighting a point in the cloud.")]
		[Range(1, 12)] public int LightSteps = 6;
		[Tooltip("How far the clouds are drawn, in metres. They dissolve over the last quarter of it, and the haze is full by four fifths of it, so the sky ends in the colour of the horizon and not on an edge.")]
		[Min(1000f)] public float MaxDistance = 44000f;
		[Tooltip("How much the last frame is kept when the clouds are steadied.")]
		[Range(0f, 0.98f)] public float TemporalBlend = 0.9f;
		[Tooltip("Metres across that the cloud shadow cookie covers.")]
		[Min(200f)] public float ShadowAreaMeters = 4000f;
		[Tooltip("Switches for taking the clouds' rendering apart, to find where a fault comes from. Read live every frame; every default is the rendering as it ships.")]
		public VolumetricCloudDiagnostics Diagnostics = new VolumetricCloudDiagnostics();
	}

	/// <summary>Whether the clouds are steadied against the last frame, over what the quality tier says.</summary>
	public enum CloudTemporalOverride
	{
		/// <summary>As the tier says (<see cref="WeatherTierSettings.CloudTemporal"/>).</summary>
		Default,
		On,
		Off,
	}

	/// <summary>The kernel the steadying rebuilds each pixel from the marched texels around it with.</summary>
	public enum CloudReconstructionKernel
	{
		/// <summary>The bilinear tent, one texel each way: what ships.</summary>
		Tent,
		/// <summary>The quadratic B-spline, a texel and a half each way: smoother, softer (option A).</summary>
		BSpline,
	}

	/// <summary>Where along its segments the light march toward the sun samples.</summary>
	public enum CloudLightMarchPhase
	{
		/// <summary>A new place in each segment for every ray and frame, averaged away by the steadying: what ships.</summary>
		Stratified,
		/// <summary>Every sample at the middle of its segment and on the same spoke, every frame.</summary>
		Fixed,
	}

	/// <summary>What the cloud march draws in place of the clouds.</summary>
	public enum CloudDebugView
	{
		/// <summary>The clouds.</summary>
		Normal,
		/// <summary>How opaque each ray found the clouds: black clear, white opaque.</summary>
		Transmittance,
		/// <summary>How many steps each ray took, blue few to red the whole budget.</summary>
		StepCount,
		/// <summary>How much light reaches the cloud each ray saw, from the light march alone: white lit, black shadowed.</summary>
		LightDepth,
		/// <summary>How much of each octave of the eddies is drawn where each ray saw cloud: red coarse, green middle, blue fine.</summary>
		DetailLod,
		/// <summary>How long the ray's steps were where it saw cloud, blue 18 m to red 1 km (logarithmic).</summary>
		StepLength,
		/// <summary>
		/// What the terrain does to the clouds along each ray: red where cloud is kept off rock, green where
		/// it is moved round the mountains (full at 500 m), blue once the scene's flow is built (dim while
		/// only its rock is).
		/// </summary>
		Terrain,
	}

	/// <summary>
	/// Switches for taking the volumetric clouds' rendering apart in the inspector, to find where a fault
	/// comes from — graininess, a change of detail that follows the camera round in a sphere, flicker —
	/// by turning one stage off or exaggerating it and watching what goes with it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Read live every frame (SkySystem publishes the march's as <c>_FishCloudDiag</c>,
	/// <c>_FishCloudDiagLight</c> and <c>_FishCloudDiagScale</c>; FishCloudsFeature folds the
	/// reconstruction's into the steadying's own parameters), so an edit in play mode shows on the next
	/// frame. Every default is the rendering exactly as it ships, and the shader reads each switch so
	/// that an unset global (all zeros) is the default too: the flags are "off" flags and the scales are
	/// sent as their difference from the default.
	/// </para>
	/// <para>
	/// None of them is a quality setting. Several cost a great deal more than the default (the finer
	/// steps, the unscaled step), and some are meant to look wrong: fixed ray phases turn whatever noise
	/// comes from the phase into bands, which is how it is told from noise that does not (with the
	/// march's steps laid out from the clouds' own edges there should be next to none of either).
	/// </para>
	/// <para>
	/// Unity keeps a field's initialiser when the asset has nothing saved for it, so a profile saved
	/// before these existed reads every default below.
	/// </para>
	/// </remarks>
	[Serializable]
	public class VolumetricCloudDiagnostics
	{
		/// <summary>The steadying's variance-clip width today, in standard deviations (FISH_RESOLVE_GAMMA).</summary>
		public const float DefaultClipGamma = 1.25f;
		/// <summary>
		/// The sky's light on the clouds as the physics has it (1): SkySystem already takes the clear sky's
		/// real share of the scene's ambient (CloudClimate.SkyShareOfAmbient), so this is a strength on top.
		/// </summary>
		public const float DefaultSkyAmbient = 1f;
		/// <summary>The ray march's early exit today: a ray stops once this little light gets through.</summary>
		public const float DefaultEarlyExit = 0.05f;
		public const float DefaultMaxStepOpticalDepth = 0.35f;
		public const float DefaultMaxInCloudGrowth = 2f;
		/// <summary>
		/// How big a change the steadying must see before a hard clip lets go of a pixel's frames
		/// (FishCloudResolve.hlsl): a tenth of the cloud's brightness, or of its opacity. 0 was the old way:
		/// any hard clip let go.
		/// </summary>
		public const float DefaultHistoryResetChange = 0.1f;
		/// <summary>
		/// The softening the composite gives the clouds, in screen pixels, as shipped: an edge-aware
		/// Gaussian over the rebuilt buffer (0 turns it off). A march a quarter of the screen across
		/// rebuilds finer than it can hold steady, and what is left of its noise — the per-ray grain and
		/// the tufts finer than a march texel — reads as a scaly texture on the cloud; the depth test keeps
		/// the blur off the world. At one pixel it was too small to see against a texture four to eight
		/// pixels across: 3 spaces the taps three pixels apart, about a nine-pixel reach. Applies at any
		/// buffer size. (It was FishCloudsFeature.BlurPixels.)
		/// </summary>
		public const float DefaultBlurPixels = 3f;

		[Header("Reconstruction (the steadying: FishCloudResolve, composite)")]
		[Tooltip("Steady the clouds against the last frame, over the quality tier's own choice. Off isolates the march: each frame's rays upscaled alone, so any grain left is the march's and not the history's, and any smear or ghost that goes with it was the history's.")]
		public CloudTemporalOverride Temporal = CloudTemporalOverride.Default;
		[Tooltip("How many frames a settled pixel averages over (16). Fewer shows each frame's noise more plainly, more averages it longer at the cost of smearing moving cloud. The profile's Temporal Blend may ask for longer (a blend b is b / (1 − b) frames), never shorter. Isolates how much of the grain is the average being too short.")]
		[Range(1, 64)] public int HistoryFrames = 16;
		[Tooltip("How far the history may stray from this frame's neighbourhood before it is clipped back, in standard deviations (1.25). Tighter lets less history through: grain that appears as it tightens is this frame's noise getting through the clip.")]
		[Range(0.25f, 4f)] public float ClipGamma = DefaultClipGamma;
		[Tooltip("Clip the history to this frame's neighbourhood. Off trusts the history wholly: grain that goes away is the clip resetting pixels (and anything that moves will ghost).")]
		public bool HistoryClip = true;
		[Tooltip("The kernel each pixel is rebuilt from the marched texels around it with: the tent (as shipped) or the smoother quadratic B-spline. Isolates blockiness and per-texel speckle in the upscale.")]
		public CloudReconstructionKernel Kernel = CloudReconstructionKernel.Tent;
		[Tooltip("The composite's edge-aware softening, in screen pixels (3). 0 turns it off and shows the rebuilt buffer as it is; more hides grain by blurring it, which says how fine the grain is.")]
		[Range(0f, 8f)] public float BlurPixels = DefaultBlurPixels;

		[Header("March (FishCloudMarch)")]
		[Tooltip("Sample each step of the march at a random place in it, new for every ray and frame (stratified, unbiased). Off samples every step at its middle. The steps are laid out from each band's edge and each cloud's own edge, not from the camera, so the clouds should stay smooth with this off: rings or terraces that appear are undersampling along the ray (try Step Scale), and grain that goes with it was the phases.")]
		public bool RayJitter = true;
		[Tooltip("Multiplies the march's step (the near step, 18–90 m, and every step grown from it: through a band's empty air, up to six times, and deep inside cloud). The limits on how far a step may climb through a thin layer and how much light it may take out of a dense cloud still apply. Under 1 is finer and slower. Grain or banding that shrinks with it is undersampling along the ray.")]
		[Range(0.25f, 4f)] public float StepScale = 1f;
		[Tooltip("Let the step grow with distance (0.6 % of the distance, 0.4 % far off). Off keeps the near step all the way (slow; the budget of steps is raised to match). The prime suspect for a change of look in a sphere round the camera: if the sphere goes, it was the step.")]
		public bool DistanceStepGrowth = true;
		[Tooltip("The most optical depth one step may take inside cloud (0.35: about 30 % of the light left). Higher is cheaper and grainier — one sample decides more of the pixel, so the ray's jitter swings it further; lower is smoother and costs more only where the cloud is dense.")]
		[Range(0.05f, 2f)] public float MaxStepOpticalDepth = DefaultMaxStepOpticalDepth;
		[Tooltip("How far a step may grow inside cloud, as a multiple of its base step (2). It was 3.5.")]
		[Range(1f, 4f)] public float MaxInCloudGrowth = DefaultMaxInCloudGrowth;
		[Tooltip("A ray stops once this little light gets through it (0.05). Lower marches deeper into dense cloud; a hard line inside thick cloud that moves with this is the exit.")]
		[Range(0.001f, 0.5f)] public float EarlyExit = DefaultEarlyExit;
		[Tooltip("March toward the light to shade each point (self-shadowing). Off lights every point as if nothing stood between it and the sun: any grain left is not the light march's.")]
		public bool LightMarch = true;
		[Tooltip("Where the light march samples: a new place in each segment for every ray and frame (as shipped), or fixed. Fixed isolates the light march's own noise.")]
		public CloudLightMarchPhase LightMarchPhase = CloudLightMarchPhase.Stratified;

		[Header("Detail / shape")]
		[Tooltip("Multiplies how strongly the eddies carve the edges, on top of the tier's Cloud Detail (1). 0 draws the edges' average without their pattern. Isolates grain that is the eddies themselves (wisps).")]
		[Range(0f, 2f)] public float DetailStrength = 1f;
		[Tooltip("Multiplies how much of a pixel's cone the finest eddies are drawn down to (SkySystem.CloudDetailConeShare, 0.5). Higher fades each octave out nearer the camera, lower keeps it further. The other suspect for a sphere round the camera: if the sphere's radius moves with this, it is the detail fading with distance.")]
		[Range(0.25f, 8f)] public float DetailLodScale = 1f;
		[Tooltip("Multiplies the width of the mixing shell round every cloud's edge (half the detail tile, about 100 m). Wider softens every edge; isolates edges that fizz because they are narrower than the step.")]
		[Range(0.25f, 4f)] public float EdgeShellScale = 1f;
		[Tooltip("Draw no eddy finer than the march's step can resolve along the ray, nor an edge sharper than it (1: the step itself; 0: the old way, the pixel's cone only). A ray samples once a step, so eddies finer than two steps and an edge sharper than one came out as a coin toss per sample — grain the frames could not average away, which is what a finer Step Scale was curing. Those octaves are drawn as their average instead, so the clouds keep their size; a finer Step Scale brings them back. Lower draws finer eddies than the step can hold, and grainier.")]
		[Range(0f, 2f)] public float DetailStepFootprint = 1f;
		[Tooltip("How much of the eddies and the cauliflower a cloud's BASE keeps (0.35). A cumulus's base is flat and smooth — it is where the rising air reaches its condensation level, one height across the whole cloud — and the turrets and ragged edges are its upper parts. Only the pattern is eased: the cloud keeps its size. 1 is the same pattern all the way up, as it was.")]
		[Range(0f, 1f)] public float BaseDetail = 0.35f;
		[Tooltip("How far up a cloud its base's smoothing reaches, as a share of the cloud's height (0.3): the pattern comes back in full over this much of it.")]
		[Range(0.05f, 1f)] public float BaseSmoothHeight = 0.3f;

		[Header("Lighting")]
		[Tooltip("How strong the sun's light diffused through the cloud is (1 is the physics: the white of a cumulus's lit side, and the 10–20 % that gets through to a thick cloud's base). 0 leaves single scattering, the sky and the ground. See Far Side Follows Light (Fixes under trial) for where it flows; turn this down if the clouds still look lit from within.")]
		[Range(0f, 2f)] public float MultipleScattering = 1f;
		[Tooltip("The droplets' diffraction spike round the sun (the silver lining). Off draws the drops' body lobe alone: sparkle toward the sun that goes with it was the spike.")]
		public bool DropletSpike = true;
		[Tooltip("How strong the sky's light on the clouds is (1: the physics). The scene's ambient is about half the sun's light by day, so a wall in shade is not black; a real clear sky gives 15–20 % at noon, rising to all of the light as the sun sets. The clouds now take only the sky's real share of the ambient (about a third of it at noon, all of it at sunset) — taken whole, as it was, their shaded sides and bases were lit two to three times over and thin cloud took the colour of the sky behind it. This multiplies that share, and the sky's part of the light the ground sends up.")]
		[Range(0f, 2f)] public float SkyAmbient = DefaultSkyAmbient;
		[Tooltip("How strong the ground's light on the clouds' bases is (1: what the ground's albedo sends back of the sun and sky on it).")]
		[Range(0f, 2f)] public float GroundBounce = 1f;

		[Header("Fixes under trial (2026-09-29 audit) — each on as shipped; off brings back the old behaviour")]
		[Tooltip("Give neighbouring places inside a march texel far-apart ray phases (their Bayer values). Off: the old bit-reversed order, which gave neighbouring places nearly the same phase, so each pixel was steadied from samples of about one phase and the step error ramped across every texel — the scale pattern four pixels across.")]
		public bool DecorrelatedRayPhase = true;
		[Tooltip("Integrate the extinction and the light linearly between consecutive samples (the trapezoid). Off: each sample's extinction held over its whole step, so where in the step the sample fell (the ray jitter) shifted the whole cloud along the ray by up to half a step — the main source of the grain. Measured on the march's twin: a lit cumulus face's grain 0.024 → 0.011, a thin sheet's 0.012 → 0.003, no bias.")]
		public bool LinearIntegration = true;
		[Tooltip("Give each pixel its own light-march phase. Off: one phase for the whole screen each frame, so the whole image's self-shadow error moved together — and a noise all neighbouring texels share moves the steadying's clip box with it, so it comes through the clip nearly whole (measured on its twin: 0.019 of a 0.02 shared noise left, against 0.002 of the same noise texel by texel). Flicker and sparkle on bright, smooth cloud.")]
		public bool PerPixelLightPhase = true;
		[Tooltip("Ease the mid-scale carve (the cauliflower) by whether the march can resolve its billows (300 m to 1 km across): only past about 80 km. Off: eased at a fixed 25–45 m footprint, which the step crosses between 8 and 15 km, so every cloud changed shape on a sphere round the camera.")]
		public bool ResolvedCarve = true;
		[Tooltip("When the light march takes fewer steps (past 8 km, and deep in cloud) keep its reach and dither the change. Off: it stopped at 792 m instead of 2880 m past 8 km — far cloud brighter on a sphere round the camera — and changed at a hard line at 30 % transmittance.")]
		public bool EvenLightReach = true;
		[Tooltip("How big a change (a share of the cloud's brightness, or of its opacity) a hard clip must be before the pixel's history is let go of (0.1). 0 is the old way: any hard clip reset the pixel, which then showed one frame's raw noise and re-converged from scratch. The box is narrowest on bright, smooth cloud, where a few per cent of noise was enough. A small help (about a tenth less noise where some of it is shared by neighbouring texels); the large one is Per Pixel Light Phase.")]
		[Range(0f, 0.5f)] public float HistoryResetChange = DefaultHistoryResetChange;
		[Tooltip("Light a heap or a tower's diffused sunlight toward the side AWAY from the sun, measured by a short march along the light (two more density reads a lit sample, about a tenth more cost where there is cloud). Off: the far side is every cloud's base, and its depth thins to nothing at every rim, so a sunlit rim reflected almost nothing and the cloud brightened from its base up, lit from within, instead of toward the sun. Decks keep their base either way: light in a sheet can only leave through the bottom.")]
		public bool FarSideFollowsLight = true;
		[Tooltip("Walk dense cloud and the rain haze under it cheaply: the haze by its optical depth, to the base and never past it; the in-cloud optical-depth limit loosened as the ray dims (1/√T), and every step limit grown once under a fifth of the light is left; deep in, most samples reuse the last light depth. Measured on the march's twin at Step Scale 0.25 and Early Exit 0.001: a storm from below 60 samples a ray → 19, an overcast deck 73 → 26, a 3 km sheet seen along its length 48 lit samples → 32, all within the twin's noise of a fine reference. The haze under a base was most of a storm's samples. Off: as it was.")]
		public bool DenseCloudEconomy = true;

		[Header("View")]
		[Tooltip("Draw one of the march's own quantities in place of the clouds (turn Temporal off to see single frames). Step Count and Step Length show a distance LOD as a ring or sphere of colour; Detail LOD shows where each octave of the eddies fades.")]
		public CloudDebugView DebugView = CloudDebugView.Normal;

		/// <summary>
		/// The march's switches (<c>_FishCloudDiag</c>): x 1 samples every step at its middle, y 1 stops the step growing
		/// with distance, z the light march (0 stratified, 1 fixed phase, 2 off), w the debug view.
		/// </summary>
		public Vector4 MarchVector => new Vector4(RayJitter ? 0f : 1f, DistanceStepGrowth ? 0f : 1f,
			!LightMarch ? 2f : LightMarchPhase == CloudLightMarchPhase.Fixed ? 1f : 0f, (float)DebugView);

		/// <summary>
		/// The lighting (<c>_FishCloudDiagLight</c>): x 1 draws the drops without their spike; y, z and w are
		/// one minus the strengths of multiple scattering, the sky and the ground, so all zeros is each at 1.
		/// </summary>
		public Vector4 LightVector => new Vector4(DropletSpike ? 0f : 1f, 1f - Mathf.Clamp(MultipleScattering, 0f, 2f), 1f - Mathf.Clamp(SkyAmbient, 0f, 2f), 1f - Mathf.Clamp(GroundBounce, 0f, 2f));

		/// <summary>
		/// The scales (<c>_FishCloudDiagScale</c>), each as its difference from the default so all zeros is
		/// the default: x detail strength − 1, y edge shell − 1, z step − 1, w early exit − 0.05.
		/// </summary>
		public Vector4 ScaleVector => new Vector4(Mathf.Clamp(DetailStrength, 0f, 2f) - 1f, Mathf.Clamp(EdgeShellScale, 0.25f, 4f) - 1f,
			Mathf.Clamp(StepScale, 0.25f, 4f) - 1f, Mathf.Clamp(EarlyExit, 0.001f, 0.5f) - DefaultEarlyExit);

		/// <summary>
		/// The in-cloud step limits (<c>_FishCloudStepTau</c>): x the most optical depth a step takes, y the most
		/// it grows, z the eddies' and the edge's footprint along the ray, in steps (0: none, the old way).
		/// </summary>
		public Vector4 StepVector => new Vector4(Mathf.Clamp(MaxStepOpticalDepth, 0.05f, 2f), Mathf.Clamp(MaxInCloudGrowth, 1f, 4f), Mathf.Clamp(DetailStepFootprint, 0f, 2f), 0f);

		/// <summary>
		/// The audit's fixes (<c>_FishCloudFix</c>), each 1 to go back to the old behaviour so all zeros is as
		/// shipped: x the step integration, y one light phase a frame, z the carve eased by the step, w the
		/// light march's shorter reach. The ray phase's order and the change that resets a history are the
		/// feature's (<see cref="DecorrelatedRayPhase"/>, <see cref="HistoryResetChange"/>).
		/// </summary>
		public Vector4 FixVector => new Vector4(LinearIntegration ? 0f : 1f, PerPixelLightPhase ? 0f : 1f, ResolvedCarve ? 0f : 1f, EvenLightReach ? 0f : 1f);

		/// <summary>
		/// More of them (<c>_FishCloudFixB</c>), 1 to go back: x the diffused sun's far side at every cloud's
		/// base, y dense cloud and its haze walked as they were.
		/// </summary>
		public Vector4 FixVectorB => new Vector4(FarSideFollowsLight ? 0f : 1f, DenseCloudEconomy ? 0f : 1f, 0f, 0f);

		/// <summary>A cloud's smooth base (<c>_FishCloudBase</c>): x 1 − Base Detail, y Base Smooth Height.</summary>
		public Vector4 BaseVector => new Vector4(1f - Mathf.Clamp01(BaseDetail), Mathf.Clamp(BaseSmoothHeight, 0.05f, 1f), 0f, 0f);

		/// <summary>Whether the clouds are steadied, given what the tier says.</summary>
		public bool TemporalFor(bool tier) => Temporal == CloudTemporalOverride.Default ? tier : Temporal == CloudTemporalOverride.On;
	}

	/// <summary>
	/// The client's weather look: materials, textures, per-kind looks and per-tier budgets.
	/// Client-only; loaded with the client's static assets.
	/// </summary>
	[CreateAssetMenu(fileName = "Weather Render Profile", menuName = "FishMMO/Weather/Render Profile", order = 20)]
	public class WeatherRenderProfile : CachedScriptableObject<WeatherRenderProfile>, ICachedObject
	{
		[Header("Precipitation")]
		public Material PrecipitationMaterial;

		[Tooltip("Rings bursting where the rain lands. Empty: no splashes.")]
		public Material SplashMaterial;
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
		[Tooltip("A tornado's condensation funnel (FishMMO/Weather/Vortex).")]
		public Material VortexMaterial;
		[Tooltip("What a tornado or a dust devil whirls up (FishMMO/Weather/Vortex Debris).")]
		public Material VortexDebrisMaterial;
		[Tooltip("Volcanic plumes: eruption columns, umbrellas and their fall where there is air, ballistic fountains where there is none (FishMMO/Weather/Volcanic Plume).")]
		public Material PlumeMaterial;
		public Material CloudCookieMaterial;

		[Header("Volumetric clouds")]
		[Tooltip("The shape and detail volumes the clouds are carved from (Weather Tools → Bake cloud noise).")]
		public Texture3D CloudShape;
		public Texture3D CloudDetail;
		public VolumetricCloudSettings Clouds = new VolumetricCloudSettings();

		[Header("Budgets per quality level (Performant, Balanced, High Fidelity)")]
		public WeatherTierSettings Performant = WeatherTierSettings.Performant();
		public WeatherTierSettings Balanced = WeatherTierSettings.Balanced();
		public WeatherTierSettings HighFidelity = WeatherTierSettings.High();

		[Header("Sky occlusion")]
		[Tooltip("What counts as a roof.")]
		public LayerMask OcclusionLayers = 1 | (1 << 7);

		[Header("Audio")]
		public WeatherAudioProfile Audio;

		[Header("Terrain instancing")]
		[Tooltip("The GPU-driven terrain tree and detail culling (FishTerrainInstancing.compute). Referenced here, not from Resources, so it ships to clients only.")]
		public ComputeShader TerrainInstancingCompute;
		[Tooltip("FishMMO/Vegetation Indirect: the procedural-instancing twin the GPU-driven terrain path draws vegetation with. Referenced so a client build includes it.")]
		public Shader VegetationIndirectShader;
		[Tooltip("FishMMO/Weather Lit Indirect: the twin for rocks, formations and ice.")]
		public Shader WeatherLitIndirectShader;
		[Tooltip("Every keyword set the vegetation and rock materials use, mapped onto the indirect twins (the art generator writes it). Nothing in a build uses those shaders directly, so without this only their keyword-free variants ship: grass loses its alpha test and draws as solid quads.")]
		public ShaderVariantCollection IndirectShaderVariants;

		[Header("Ground colour under vegetation")]
		[Tooltip("How much of the ground's colour a grass blade or leaf takes at its root (GroundColourMap). Live: changes apply while playing.")]
		[Range(0f, 1f)] public float GroundRootBlend = 1f;
		[Tooltip("How much of the ground's colour the rest of a low plant takes, above Ground Blend Height. Lit grass reads lighter than the soil at the same colour (normals up, translucency, no ground occlusion), so this wants to be high.")]
		[Range(0f, 1f)] public float GroundPlantBlend = 0.9f;
		[Tooltip("Metres above the root over which the blend eases from the root's to the plant's.")]
		[Min(0.05f)] public float GroundBlendHeight = 0.8f;
		[Tooltip("How much of a blade's own light and dark (texture, darker root) survives the blend, as darkening only: 0 = flat ground colour, 0.3 = up to 30% darker in the blade's dark parts.")]
		[Range(0f, 0.6f)] public float GroundBladeDetail = 0.25f;
		[Tooltip("Metres above the root where the blend starts fading out, and where it is gone: tree crowns keep their own colour.")]
		[Min(0f)] public float GroundCrownStart = 1.5f;
		[Min(0.1f)] public float GroundCrownEnd = 3f;

		[Header("Distant terrain objects (trees, plants, terrain rocks)")]
		[Tooltip("Metres from the camera where distant trees, plants and terrain rocks start settling into the landscape: lighting normal flattening toward up (no black shadowed sides against a flat-lit hillside) and colour drifting toward the local ground's. Live.")]
		[Min(0f)] public float DistanceBlendStart = 80f;
		[Tooltip("Metres where that is complete.")]
		[Min(1f)] public float DistanceBlendEnd = 600f;
		[Tooltip("How far their lighting normal flattens to up at full distance.")]
		[Range(0f, 1f)] public float DistanceNormalFlatten = 0.6f;
		[Tooltip("How much of the ground's colour they take at full distance (0 keeps their own).")]
		[Range(0f, 1f)] public float DistanceGroundPull = 0.35f;

		[Header("Procedural grass")]
		[Tooltip("FishGrassBlades.compute: generates the visible blades around each camera every frame. Referenced here, not from Resources, so it ships to clients only.")]
		public ComputeShader GrassBladesCompute;
		[Tooltip("FishMMO/Grass Blades: the blade strips drawn from the compute's output.")]
		public Shader GrassBladesShader;
		public GrassBladeSettings Grass = new GrassBladeSettings();

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
