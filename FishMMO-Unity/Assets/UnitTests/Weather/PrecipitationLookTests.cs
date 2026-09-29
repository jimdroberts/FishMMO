using System.Globalization;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Client;
using FishMMO.Shared.Weather;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// How what falls is lit: the drops and grains round the camera, their splashes, and the curtain
	/// under a distant storm. Shapes and ranges, not spot values — the physics is in the remarks of
	/// <see cref="PrecipitationField.ClearDropSun"/>, <see cref="PrecipitationField.GrainSun"/> and
	/// <see cref="CurtainPresenter.DiffuseLight"/>.
	/// </summary>
	[TestFixture]
	public class PrecipitationLookTests
	{
		private const string PrecipitationShader = "Assets/Prefabs/Client/Weather/Shaders/FishPrecipitation.shader";
		private const string SplashShader = "Assets/Prefabs/Client/Weather/Shaders/FishPrecipitationSplash.shader";
		private const string CurtainShader = "Assets/Prefabs/Client/Weather/Shaders/FishCurtain.shader";

		// ── Round the camera ─────────────────────────────────────────────

		[Test]
		public void ADropShowsTheSunOnlyAgainstTheLight()
		{
			/* A raindrop is a lens: it shows what lies behind it, so the sun is in it only when the sun
			 * is beyond it, and most when straight beyond. */
			LogAssert.IsTrue(Mathf.Abs(PrecipitationField.ClearDropSun(1f) - 1f) < 1e-5f, "straight against the sun, a drop shows all of it");
			LogAssert.IsTrue(PrecipitationField.ClearDropSun(0f) <= 1e-6f, "side-on, none");
			LogAssert.IsTrue(PrecipitationField.ClearDropSun(-1f) <= 1e-6f, "and with the sun behind the camera, none");
			float last = -1f;
			for (float c = -1f; c <= 1f; c += 0.05f)
			{
				float sun = PrecipitationField.ClearDropSun(c);
				LogAssert.IsTrue(sun >= last - 1e-6f && sun >= 0f && sun <= 1f, $"more of the sun the more it is behind the drop (cos {c:0.00}: {sun:0.000})");
				last = sun;
			}
		}

		[Test]
		public void AGrainIsLitOnTheFaceItShows()
		{
			/* A flake, a hailstone, a grain of sand: an opaque tumbling thing, a Lambertian sphere on
			 * average. Full phase is two thirds of a card facing the sun; new phase is dark. */
			LogAssert.IsTrue(Mathf.Abs(PrecipitationField.GrainSun(1f) - 2f / 3f) < 1e-4f, $"with the sun behind the camera the disc averages the cosine: {PrecipitationField.GrainSun(1f):0.0000}");
			LogAssert.IsTrue(PrecipitationField.GrainSun(-1f) < 1e-4f, "with the sun behind the grain its lit face is turned away");
			LogAssert.IsTrue(Mathf.Abs(PrecipitationField.GrainSun(0f) - 2f / (3f * Mathf.PI)) < 1e-4f, "at quarter phase, 2/3π");
			float last = 1f;
			for (float degrees = 0f; degrees <= 180f; degrees += 5f)
			{
				float sun = PrecipitationField.GrainSun(Mathf.Cos(degrees * Mathf.Deg2Rad));
				LogAssert.IsTrue(sun <= last + 1e-5f && sun >= -1e-5f, $"darker as the phase angle opens ({degrees:0}°: {sun:0.000})");
				last = sun;
			}
		}

		[Test]
		public void RainIsSeenThrough_EverythingElseIsAGrain()
		{
			LogAssert.IsTrue(PrecipitationField.TraitsOf(WeatherChannel.RainWeight).Clear, "a raindrop is clear");
			LogAssert.IsFalse(PrecipitationField.TraitsOf(WeatherChannel.SnowWeight).Clear, "a flake scatters in its lattice");
			LogAssert.IsFalse(PrecipitationField.TraitsOf(WeatherChannel.HailWeight).Clear, "hail's milky layers are opaque");
			LogAssert.IsFalse(PrecipitationField.TraitsOf(WeatherChannel.AshWeight).Clear, "ash is a grain");
			LogAssert.IsFalse(PrecipitationField.TraitsOf(WeatherChannel.SandWeight).Clear, "sand is a grain");
			string field = SourceScanPins.ReadCode("Assets/Scripts/Client/World/Weather/Presentation/PrecipitationField.cs");
			LogAssert.IsTrue(field.Contains("traits.Clear ? 1f : 0f"), "and the shader is told which");
		}

		[Test]
		public void WaterColoursNothing_DustIsItsOwnColour()
		{
			foreach (PrecipitationLook look in new[] { PrecipitationLook.Rain(), PrecipitationLook.Snow(), PrecipitationLook.Hail() })
			{
				LogAssert.IsTrue(Mathf.Abs(look.Tint.r - look.Tint.b) < 1e-3f && Mathf.Abs(look.Tint.g - look.Tint.b) < 1e-3f,
					$"water and ice are neutral: the sky's light carries the hue ({look.Tint})");
			}
			LogAssert.IsTrue(PrecipitationLook.Sand().Tint.r > PrecipitationLook.Sand().Tint.b, "sand is sand-coloured");
		}

		[Test]
		public void NothingThatFallsIsLitByTheBareSun()
		{
			/* The sun's light is handed over with the cloud in view divided back out of it, for the
			 * cloud shadow cookie to carry. Read without the cookie it is the open sun under a storm —
			 * the orange streaks and splashes. Every weather shader that lights with the main light
			 * reads its cookie. */
			string precipitation = SourceScanPins.ReadCode(PrecipitationShader);
			string splash = SourceScanPins.ReadCode(SplashShader);
			string curtain = SourceScanPins.ReadCode(CurtainShader);
			LogAssert.IsTrue(precipitation.Contains("_LIGHT_COOKIES") && precipitation.Contains("_MainLightCookieTexture"), "the drops read the cloud shadow on the sun");
			LogAssert.IsTrue(precipitation.Contains("mainLight.color * PrecipSunCookie("), "and light by the sun through it");
			LogAssert.IsFalse(precipitation.Contains("mainLight.color * 0.35"), "not a fixed share of the bare light");
			LogAssert.IsTrue(splash.Contains("_LIGHT_COOKIES") && splash.Contains("mainLight.color * SplashSunCookie("), "nor the splashes");
			LogAssert.IsFalse(splash.Contains("mainLight.color * 0.35"), "the splashes neither");
			LogAssert.IsTrue(curtain.Contains("_LIGHT_COOKIES") && curtain.Contains("light.color * CurtainCookie("), "nor a distant shaft");

			/* And not through the air's hue twice: the sky's light already carries it. */
			LogAssert.IsFalse(SourceScanPins.ReadCode("Assets/Scripts/Client/World/Weather/Presentation/PrecipitationField.cs").Contains("InAir("), "the drops' tint is their own");
			LogAssert.IsFalse(SourceScanPins.ReadCode("Assets/Scripts/Client/World/Weather/Presentation/PrecipitationSplashField.cs").Contains("InAir("), "the splashes' too");
		}

		[Test]
		public void TheShaderLightsDropsAndGrainsAsTheModelSays()
		{
			string precipitation = SourceScanPins.ReadCode(PrecipitationShader);
			LogAssert.IsTrue(precipitation.Contains("FishTrilight(-view)") && precipitation.Contains("max(0.0, dot(-view, mainLight.direction))"),
				"a clear drop shows the sky beyond it and the sun behind it (ClearDropSun)");
			LogAssert.IsTrue(precipitation.Contains("FishTrilight(view)") && precipitation.Contains("PrecipGrainSun(dot(view, mainLight.direction))"),
				"a grain shows the sky and sun on the face toward the camera (GrainSun)");
			LogAssert.IsTrue(precipitation.Contains("(2.0 / 3.0) * (sin(alpha) + (PI - alpha) * cos(alpha)) / PI"), "the Lambert sphere, as GrainSun has it");
		}

		// ── A distant storm ──────────────────────────────────────────────

		[Test]
		public void AStormLetsDownAFewPerCentOfTheDay()
		{
			float through = CurtainPresenter.StormCloudThrough;
			LogAssert.IsTrue(through > 0.01f && through < 0.06f, $"the light under a cumulonimbus is a few per cent of the day's: {through:0.000}");
			Match define = Regex.Match(SourceScanPins.ReadCode(CurtainShader), @"#define CURTAIN_STORM_THROUGH ([0-9.]+)");
			LogAssert.IsTrue(define.Success, "the shader carries it");
			float shader = float.Parse(define.Groups[1].Value, CultureInfo.InvariantCulture);
			LogAssert.IsTrue(Mathf.Abs(shader - through) < 5e-4f, $"as the same figure: {shader} against {through:0.0000}");
		}

		[Test]
		public void TheOpenSkyShowsMoreTheFurtherBelowTheBase()
		{
			LogAssert.IsTrue(CurtainPresenter.OpenSkyShare(0f, 3000f) < 1e-4f, "right under the base, none");
			LogAssert.IsTrue(Mathf.Abs(CurtainPresenter.OpenSkyShare(500f, 0f) - 1f) < 1e-4f, "at the edge, all of it");
			float last = -1f;
			for (float gap = 0f; gap <= 3000f; gap += 100f)
			{
				float share = CurtainPresenter.OpenSkyShare(gap, 2000f);
				LogAssert.IsTrue(share >= last && share >= 0f && share <= 1f, $"more the further down ({gap:0} m: {share:0.000})");
				last = share;
			}
			LogAssert.IsTrue(CurtainPresenter.OpenSkyShare(1000f, 500f) > CurtainPresenter.OpenSkyShare(1000f, 4000f), "and the nearer the edge");
		}

		[Test]
		public void TheEdgeIsWhereTheCoverFallsThroughAHalf()
		{
			const float reach = 2000f;
			LogAssert.IsTrue(CurtainPresenter.EdgeDistance(0.3f, 0f, reach) <= 0f, "past the edge already: none");
			LogAssert.IsTrue(Mathf.Abs(CurtainPresenter.EdgeDistance(1f, 0f, reach) - reach * 0.5f) < 1f, "cover falling from all to none over a reach: half a reach");
			LogAssert.IsTrue(Mathf.Abs(CurtainPresenter.EdgeDistance(1f, 0.5f, reach) - reach) < 1f, "falling to a half there: a reach");
			LogAssert.IsTrue(Mathf.Abs(CurtainPresenter.EdgeDistance(1f, 1f, reach) - 4f * reach) < 1f, "not falling at all: as far as it is ever put");
			LogAssert.IsTrue(CurtainPresenter.EdgeDistance(1f, 0.2f, reach) < CurtainPresenter.EdgeDistance(1f, 0.7f, reach), "the faster the cover falls, the nearer the edge");
		}

		/// <summary>An ordinary clear afternoon round the storm, in the units the world is lit in.</summary>
		private static Color Diffuse(float gap, float height, float edge, float overhead)
		{
			var sky = new Color(0.3f, 0.34f, 0.42f);
			var horizon = new Color(0.62f, 0.66f, 0.72f);
			var sun = new Color(1.5f, 1.45f, 1.35f);
			const float sunUp = 0.7f;
			Color ground = (sky + sun * sunUp) * 0.2f;
			return CurtainPresenter.DiffuseLight(sky, horizon, ground, sun, sunUp, gap, height, edge, overhead);
		}

		private static float Level(Color c) => c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;

		[Test]
		public void AShaftIsLitByTheDayRoundItsStorm_DarkUnderItButNeverBlack()
		{
			const float top = 1500f;
			float open = Level(Diffuse(750f, 750f, 0f, 0f));
			float core = Level(Diffuse(750f, 750f, 20000f, 1f));
			LogAssert.IsTrue(core < 0.25f * open, $"deep under the storm it gets a fraction of the open day: {core:0.000} against {open:0.000}");
			float cloudBase = Level(new Color(0.3f, 0.34f, 0.42f) + new Color(1.5f, 1.45f, 1.35f) * 0.7f) * CurtainPresenter.StormCloudThrough;
			LogAssert.IsTrue(core > 0.5f * cloudBase, $"but never black: about what the base lets down, {core:0.000} against {cloudBase:0.000}");

			/* Paler toward the ground, where more of the horizon shows under the base's edge, and
			 * toward its sides. */
			float last = 0f;
			for (float gap = 100f; gap <= top - 100f; gap += 100f)
			{
				float here = Level(Diffuse(gap, top - gap, 3000f, 1f));
				LogAssert.IsTrue(here >= last - 1e-4f, $"paler the further down the shaft ({gap:0} m under the base: {here:0.000})");
				last = here;
			}
			LogAssert.IsTrue(Level(Diffuse(1000f, 500f, 500f, 1f)) > Level(Diffuse(1000f, 500f, 5000f, 1f)), "and nearer its storm's edge");
			LogAssert.IsTrue(Level(Diffuse(1000f, 500f, 3000f, 1f)) <= open + 1e-4f, "never brighter than the open day");
		}

		[Test]
		public void WaterIsWhiteInTheCurtain_DustIsItsOwnColour()
		{
			var fog = new Color(0.55f, 0.6f, 0.66f);
			foreach (PrecipitationKind kind in new[] { PrecipitationKind.Rain, PrecipitationKind.Snow, PrecipitationKind.Hail })
			{
				Color c = CurtainPresenter.ScatteringColour(kind, fog);
				LogAssert.IsTrue(c.r > 0.999f && c.g > 0.999f && c.b > 0.999f, $"{kind} takes nothing out of the light it scatters");
			}
			var ash = new Color(0.42f, 0.4f, 0.38f);
			LogAssert.IsTrue(CurtainPresenter.ScatteringColour(PrecipitationKind.Ash, ash) == new Color(ash.r, ash.g, ash.b, 1f), "ash is the colour it is");
		}

		[Test]
		public void TheCurtainIsLitAndHazedAsItsStormsCloudIs()
		{
			string curtain = SourceScanPins.ReadCode(CurtainShader);
			LogAssert.IsTrue(curtain.Contains("lights.sky = _FishCloudAmbient.rgb") && curtain.Contains("lights.horizon = _FishCloudHaze.rgb"),
				"lit by the day the storm's cloud is lit by, not the viewer's ambient");
			LogAssert.IsTrue(curtain.Contains("exp(-CurtainAirDepth(") && curtain.Contains("_FishCloudAerosol.x * haze"),
				"and seen through the air the cloud is seen through (FishCloudAirDepth)");
			LogAssert.IsTrue(curtain.Contains("return gap / max(1e-3, sqrt(gap * gap + edge * edge));"), "OpenSkyShare's twin");
			LogAssert.IsTrue(curtain.Contains("min(4.0 * reach, reach * (here - 0.5) / fall) : 4.0 * reach"), "EdgeDistance's twin");
			LogAssert.IsTrue(curtain.Contains("float3 upper = lights.horizon * open + onOpen * (through * (1.0 - open));")
				&& curtain.Contains("float3 lower = lights.ground * (openGround + (1.0 - openGround) * through);"), "DiffuseLight's twin");
			string presenter = SourceScanPins.ReadCode("Assets/Scripts/Client/World/Weather/Sky/SkyEffects.cs");
			LogAssert.IsTrue(presenter.Contains("SkySystem.CloudsReady ? 1f : 0f, 0f, 0f));"), "and told when the sky has published that day");
			LogAssert.IsTrue(presenter.Contains("ScatteringColour(kind, "), "and what falls is given its own albedo");
		}
	}
}
