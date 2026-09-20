using UnityEngine;

namespace FishMMO.Shared.Celestial
{
	/// <summary>The sky's colours and lights at one sun altitude.</summary>
	public struct SkySample
	{
		public Color Zenith;
		public Color Horizon;
		public Color Ground;
		public Color Fog;
		public Color AmbientSky;
		public Color AmbientEquator;
		public Color AmbientGround;
		public Color SunLight;
		public float SunIntensity;
		public Color MoonLight;
		public float MoonIntensity;
		public Color CloudLit;
		public Color CloudShadow;
		public float StarVisibility;

		public static SkySample Lerp(in SkySample a, in SkySample b, float t)
		{
			return new SkySample
			{
				Zenith = Color.Lerp(a.Zenith, b.Zenith, t),
				Horizon = Color.Lerp(a.Horizon, b.Horizon, t),
				Ground = Color.Lerp(a.Ground, b.Ground, t),
				Fog = Color.Lerp(a.Fog, b.Fog, t),
				AmbientSky = Color.Lerp(a.AmbientSky, b.AmbientSky, t),
				AmbientEquator = Color.Lerp(a.AmbientEquator, b.AmbientEquator, t),
				AmbientGround = Color.Lerp(a.AmbientGround, b.AmbientGround, t),
				SunLight = Color.Lerp(a.SunLight, b.SunLight, t),
				SunIntensity = Mathf.Lerp(a.SunIntensity, b.SunIntensity, t),
				MoonLight = Color.Lerp(a.MoonLight, b.MoonLight, t),
				MoonIntensity = Mathf.Lerp(a.MoonIntensity, b.MoonIntensity, t),
				CloudLit = Color.Lerp(a.CloudLit, b.CloudLit, t),
				CloudShadow = Color.Lerp(a.CloudShadow, b.CloudShadow, t),
				StarVisibility = Mathf.Lerp(a.StarVisibility, b.StarVisibility, t),
			};
		}
	}

	/// <summary>
	/// How a sky looks: colours for every sun altitude from deep night (−18°) to noon (90°),
	/// light colours and strengths, disc sizes, stars, clouds and aurora. A body's sky, a
	/// scene's override, or a region's (through ChangeSkyProfileAction).
	/// </summary>
	[CreateAssetMenu(fileName = "Sky Profile", menuName = "FishMMO/World/Sky Profile", order = 17)]
	public class SkyProfile : CachedScriptableObject<SkyProfile>, ICachedObject
	{
		public const float LowestAltitude = -18f;
		public const float HighestAltitude = 90f;

		[Header("Colours by sun altitude (left: −18°, right: +90°)")]
		public Gradient Zenith = Make(new Color(0.01f, 0.012f, 0.03f), new Color(0.06f, 0.08f, 0.2f), new Color(0.22f, 0.32f, 0.58f), new Color(0.25f, 0.45f, 0.85f), new Color(0.2f, 0.42f, 0.86f));
		public Gradient Horizon = Make(new Color(0.02f, 0.025f, 0.05f), new Color(0.35f, 0.22f, 0.3f), new Color(0.98f, 0.55f, 0.3f), new Color(0.72f, 0.8f, 0.92f), new Color(0.7f, 0.82f, 0.95f));
		public Gradient Ground = Make(new Color(0.01f, 0.01f, 0.012f), new Color(0.08f, 0.07f, 0.08f), new Color(0.25f, 0.22f, 0.2f), new Color(0.36f, 0.36f, 0.35f), new Color(0.4f, 0.4f, 0.38f));
		public Gradient Fog = Make(new Color(0.02f, 0.025f, 0.04f), new Color(0.2f, 0.16f, 0.2f), new Color(0.75f, 0.55f, 0.42f), new Color(0.66f, 0.72f, 0.8f), new Color(0.68f, 0.75f, 0.84f));
		public Gradient AmbientSky = Make(new Color(0.03f, 0.04f, 0.08f), new Color(0.12f, 0.12f, 0.2f), new Color(0.45f, 0.4f, 0.45f), new Color(0.55f, 0.62f, 0.75f), new Color(0.6f, 0.68f, 0.82f));
		public Gradient AmbientEquator = Make(new Color(0.02f, 0.025f, 0.04f), new Color(0.1f, 0.08f, 0.12f), new Color(0.45f, 0.32f, 0.25f), new Color(0.42f, 0.44f, 0.46f), new Color(0.45f, 0.47f, 0.5f));
		public Gradient AmbientGround = Make(new Color(0.01f, 0.01f, 0.015f), new Color(0.04f, 0.04f, 0.05f), new Color(0.15f, 0.12f, 0.1f), new Color(0.2f, 0.2f, 0.19f), new Color(0.22f, 0.22f, 0.2f));
		public Gradient SunLight = Make(new Color(1f, 0.35f, 0.15f), new Color(1f, 0.4f, 0.18f), new Color(1f, 0.62f, 0.38f), new Color(1f, 0.93f, 0.84f), new Color(1f, 0.97f, 0.92f));
		public Gradient CloudLit = Make(new Color(0.08f, 0.09f, 0.12f), new Color(0.35f, 0.25f, 0.3f), new Color(1f, 0.7f, 0.5f), new Color(0.98f, 0.98f, 1f), new Color(1f, 1f, 1f));
		public Gradient CloudShadow = Make(new Color(0.02f, 0.02f, 0.03f), new Color(0.12f, 0.1f, 0.14f), new Color(0.4f, 0.3f, 0.35f), new Color(0.55f, 0.58f, 0.65f), new Color(0.58f, 0.62f, 0.7f));

		[Header("Lights")]
		[Tooltip("Sun intensity by sun altitude in degrees.")]
		public AnimationCurve SunIntensity = new AnimationCurve(new Keyframe(-6f, 0f), new Keyframe(0f, 0.15f), new Keyframe(10f, 0.8f), new Keyframe(40f, 1.2f), new Keyframe(90f, 1.3f));
		[Tooltip("Moon intensity at full, by sun altitude in degrees (the moon fades as the sky brightens).")]
		public AnimationCurve MoonIntensity = new AnimationCurve(new Keyframe(-18f, 0.25f), new Keyframe(-6f, 0.18f), new Keyframe(0f, 0f), new Keyframe(90f, 0f));
		public Color MoonLight = new Color(0.62f, 0.7f, 0.9f);

		[Header("Discs and stars")]
		[Tooltip("Off (the default): every disc is life-size — a moon is as big in the sky as its radius and distance make it. On: discs are drawn at the scales below, the way most games flatter the sky.")]
		public bool LargerThanLife;
		[Tooltip("Drawn size of suns, when Larger than life is on.")]
		[Min(0.1f)] public float SunDiscScale = 1.6f;
		[Tooltip("Drawn size of moons and planets, when Larger than life is on.")]
		[Min(0.1f)] public float BodyDiscScale = 1.6f;
		[Range(0f, 4f)] public float StarBrightness = 1f;
		[Tooltip("How bright the galaxy is in the night sky. It scales whichever galaxy is drawn: the supplied image, or the built-in band when there is none.")]
		[Range(0f, 2f)] public float MilkyWay = 0.6f;
		[Tooltip("Your own galaxy for the night sky. Left empty, the sky draws its own Milky Way band. A cubemap because the star field it sits in is one too, so it turns with the stars and has no seam or pinched pole; an equirectangular panorama works by setting its importer's Texture Shape to Cube (Latitude-Longitude).")]
		public Cubemap Galaxy;
		[Range(0f, 1f)] public float StarTwinkle = 0.4f;
		[Tooltip("Sky exposure multiplier.")]
		[Min(0f)] public float Exposure = 1f;

		[Tooltip("How bright the halo around the sun is: the light the air scatters forward at you, which is what makes the sky near the sun paler than the rest of it. This is the broad one — it reaches tens of degrees out and is most of what \"the sun is too bright\" means in a clear sky. The disc and the horizon glow are separate.")]
		[Range(0f, 2f)] public float SunHalo = 0.6f;
		[Tooltip("How bright the sun's own disc is. It is a third of a degree across, so this only decides how hard that spot clips to white.")]
		[Range(0f, 40f)] public float SunDisc = 18f;
		[Tooltip("The glow along the horizon toward a low sun. It only shows within about fourteen degrees of the horizon, on both the sun's side and the view's, so it is a sunset term and does nothing at midday.")]
		[Range(0f, 1f)] public float SunGlow = 0.35f;

		[Header("Clouds")]
		[Tooltip("Size of the cloud pattern: larger is bigger clouds.")]
		[Min(0.1f)] public float CloudScale = 1f;
		[Range(0f, 1f)] public float CirrusAmount = 0.35f;

		[Header("Aurora")]
		public Color AuroraA = new Color(0.2f, 1f, 0.45f);
		public Color AuroraB = new Color(0.55f, 0.25f, 1f);
		[Tooltip("Aurora needs at least this latitude (either hemisphere), in degrees.")]
		[Range(0f, 90f)] public float AuroraMinLatitude = 45f;

		[Header("No atmosphere")]
		[Tooltip("Use a black sky with stars at noon (bodies without air).")]
		public bool Airless;

		/// <summary>
		/// Forces <see cref="LargerThanLife"/> on or off for every profile, without touching the
		/// assets: the test beds use it to compare the two. Null (the default) leaves each profile
		/// to its own setting, which is what the game does.
		/// </summary>
		public static bool? LargerThanLifeOverride;

		private bool Exaggerated => LargerThanLifeOverride ?? LargerThanLife;

		/// <summary>How much bigger than life a sun is drawn: 1 unless the profile flatters the sky.</summary>
		public float SunScale => Exaggerated ? SunDiscScale : 1f;

		/// <summary>How much bigger than life a moon or planet is drawn.</summary>
		public float BodyScale => Exaggerated ? BodyDiscScale : 1f;

		public static float AltitudeKey(float altitudeDegrees) => Mathf.InverseLerp(LowestAltitude, HighestAltitude, altitudeDegrees);

		/// <summary>Colours at a sun altitude, in degrees.</summary>
		public SkySample Evaluate(float sunAltitude)
		{
			float t = AltitudeKey(sunAltitude);
			var sample = new SkySample
			{
				Zenith = Zenith.Evaluate(t) * Exposure,
				Horizon = Horizon.Evaluate(t) * Exposure,
				Ground = Ground.Evaluate(t),
				Fog = Fog.Evaluate(t),
				AmbientSky = AmbientSky.Evaluate(t),
				AmbientEquator = AmbientEquator.Evaluate(t),
				AmbientGround = AmbientGround.Evaluate(t),
				SunLight = SunLight.Evaluate(t),
				SunIntensity = Mathf.Max(0f, SunIntensity.Evaluate(sunAltitude)),
				MoonLight = MoonLight,
				MoonIntensity = Mathf.Max(0f, MoonIntensity.Evaluate(sunAltitude)),
				CloudLit = CloudLit.Evaluate(t),
				CloudShadow = CloudShadow.Evaluate(t),
				StarVisibility = Mathf.Clamp01(Mathf.InverseLerp(-2f, -14f, sunAltitude)),
			};
			if (Airless)
			{
				sample.Zenith = new Color(0.003f, 0.003f, 0.006f);
				sample.Horizon = new Color(0.006f, 0.006f, 0.01f);
				sample.Fog = sample.Horizon;
				sample.StarVisibility = 1f;
				sample.SunLight = Color.white;
				sample.SunIntensity = sunAltitude > -1f ? 1.4f : 0f;
			}
			return sample;
		}

		/// <summary>Five keys at −18°, −6°, 0°, 20° and 90°.</summary>
		public static Gradient Make(Color night, Color twilight, Color sunset, Color day, Color noon)
		{
			var gradient = new Gradient();
			gradient.SetKeys(new[]
			{
				new GradientColorKey(night, AltitudeKey(-18f)),
				new GradientColorKey(twilight, AltitudeKey(-6f)),
				new GradientColorKey(sunset, AltitudeKey(0f)),
				new GradientColorKey(day, AltitudeKey(20f)),
				new GradientColorKey(noon, AltitudeKey(90f)),
			}, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
			return gradient;
		}
	}
}
