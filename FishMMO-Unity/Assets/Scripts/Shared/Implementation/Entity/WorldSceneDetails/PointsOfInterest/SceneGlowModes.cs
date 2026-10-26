using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// How a cold glow's brightness moves (2026-10-25, I6i; decisions 33, 40, 43, 46, 53, 56–59, 61, 64, 65, 68–76, 81, 83, 86,
	/// 89, 90, 92, 93, 95): each a pure function of the shared clock (and, where named, of the sky's and the weather's state, which
	/// are themselves pure functions of the world clock). APPEND-ONLY: the values are stored in biome templates and the GPU's
	/// glow table. <see cref="Default"/> is what every glow did before: a way glow pulses (<see cref="SceneGlow.Pulse"/>), a
	/// stormglass flickers, a cave crystal shines steady.
	/// </summary>
	public enum SceneGlowMode : byte
	{
		/// <summary>Today's: a way glow's pulse, a flickering crystal's flicker, a steady crystal's steadiness.</summary>
		Default = 0,
		/// <summary>The soft two-beat pulse (<see cref="SceneGlow.Pulse"/>).</summary>
		Pulse = 1,
		/// <summary>The stormglass's crackling flicker (<see cref="SceneGlow.Flicker"/>, decision 33).</summary>
		Flicker = 2,
		/// <summary>Steady, at the brightness it was made for (decision 70).</summary>
		Still = 3,
		/// <summary>A quick rippling shimmer, as sunlight through moving water (decision 40).</summary>
		Caustic = 4,
		/// <summary>One notch brighter each day of a count of <c>Every</c> days, then back to the first (decision 56).</summary>
		Step = 5,
		/// <summary>Follows the real sun's height: brightest at noon, dim at night (decision 57). Carries its own daylight.</summary>
		Sun = 6,
		/// <summary>Follows the wet half of the year (decision 59).</summary>
		Season = 7,
		/// <summary>Follows the wind's strength (decisions 64, 69).</summary>
		Wind = 8,
		/// <summary>A deep eight-second breath, fuller while the dawn mist is up (decision 61).</summary>
		Breath = 9,
		/// <summary>A lava crust's overturn: a sudden flare, then the crust cooling dark (decision 65).</summary>
		Overturn = 10,
		/// <summary>Brightest as the sun sets, fading through the night toward dawn, dark by day (decision 58). Carries its own daylight.</summary>
		AfterSunset = 11,
		/// <summary>Rises and falls with the sea's swell running before the prevailing wind (decision 68).</summary>
		Swell = 12,
		/// <summary>Brightens twice in every turn of the giant's magnetic field over the moon (decision 71).</summary>
		Field = 13,
		/// <summary>Lit only while the sun is on the horizon: sunrise and sunset (decision 72). Carries its own daylight.</summary>
		Terminator = 14,
		/// <summary>Follows the phase of the parent world in the sky (decision 72).</summary>
		Volvalight = 15,
		/// <summary>Follows the rain the weather will bring in the next six hours (decision 75).</summary>
		Rain = 16,
		/// <summary>A point of light creeping along the nearest stored landform line (decision 76; along a fixed bearing where a scene stores none).</summary>
		Trail = 17,
		/// <summary>Follows how long the night is: brightest in the polar night (decision 81).</summary>
		NightLength = 18,
		/// <summary>The inverse of <see cref="Sun"/>: brightest at midnight (decision 83). Carries its own daylight.</summary>
		Night = 19,
		/// <summary>Open only on dark-moon nights (decision 86).</summary>
		Eyelid = 20,
		/// <summary>Lit one day in <c>Every</c> (decision 89).</summary>
		DayCount = 21,
		/// <summary>A crack's tidal tension, the stress rotating through each orbit, phased by the nearest stored crack's bearing (decision 90; a smooth field where none is stored).</summary>
		Kneading = 22,
		/// <summary>Brighter as the fog thickens (decision 74).</summary>
		Fog = 23,
		/// <summary>Follows the plume tide of the moon's orbit (decision 53, <c>_FishPlumeTide</c>).</summary>
		PlumeTide = 24,
		/// <summary>Glows through the sea floor's turbidity episodes (decision 92, <see cref="Weather.TurbidityEpisodes"/>).</summary>
		Tremor = 25,
		/// <summary>Follows the nearest stored fountain vent's episodes (decision 93, <see cref="Biomes.BiomeFountainEpisodes"/>; its 500 m cell's where none is stored).</summary>
		Episode = 26,
		/// <summary>Flares with the scene's Strombolian vent's bursts (decision 95, <see cref="Weather.VentBursts"/>, <c>_FishVentBurst</c>).</summary>
		Burst = 27,
	}

	/// <summary>
	/// Which glows a biome's mode entry moves (<see cref="Biomes.BiomeGlowMode"/>). APPEND-ONLY (stored). The GPU tells them apart
	/// by material: a way piece's glowing part (_FishStructure 3) or a glowing crystal (_FishStructure −1 flickering, −2 steady).
	/// </summary>
	public enum SceneGlowSource : byte
	{
		/// <summary>The faerie and rune glows: a glowjar's mote, a rune stone's runes (and their lights).</summary>
		Way = 0,
		/// <summary>The glowing crystals: cave crystals, a crystal post's crystal (and the lights they cast).</summary>
		Crystal = 1,
	}

	/// <summary>
	/// What the glow modes read of the sky and the weather at a moment (published once a frame by the client: GlowSky, as the
	/// globals <c>_FishGlowSkyA..D</c>, <c>_FishPlumeTide</c>, <c>_FishTidalStress</c>). Every field a pure function of the world
	/// clock at the scene (the weather's sampled at the scene's origin on whole knots of the clock and eased between them), so
	/// every player in a scene reads the same numbers.
	/// </summary>
	public struct SceneGlowSky
	{
		/// <summary>The main sun's height: the sine of its elevation.</summary>
		public float SunHeight;
		/// <summary>Local solar time, a fraction of the day (0.5 noon).</summary>
		public float LocalTime01;
		/// <summary>The share of today that is night (1 the polar night, 0 the midnight sun).</summary>
		public float NightShare;
		/// <summary>Local days since the epoch, modulo <see cref="SceneGlowModes.DayWrap"/> (exact in a float).</summary>
		public float Day;
		/// <summary>The brightest moon's lit share (0 dark moon or no moon, 1 full).</summary>
		public float MoonLight;
		/// <summary>The parent world's lit share in a moon's sky (0 none).</summary>
		public float ParentLight;
		/// <summary>The parent's magnetic field's turn over the moon, a fraction of a turn.</summary>
		public float FieldTurn;
		/// <summary>The moon's way round its orbit, a fraction of a turn from its offset (the 33 h fallback for a planet).</summary>
		public float OrbitTurn;
		/// <summary>How far into the wet half of the year (0 the driest day, 1 the wettest).</summary>
		public float Wet;
		/// <summary>The wind, 0 calm … 1 at 15 m/s and over.</summary>
		public float Wind;
		/// <summary>The rain to come in the next six hours, 0..1.</summary>
		public float Rain;
		/// <summary>How much fog, 0..1.</summary>
		public float Fog;
		/// <summary>The sea's state: the wind of the last three hours, 0..1.</summary>
		public float Sea;
		/// <summary>The scene's turbidity episode, 0 quiet … 1 at its height (<see cref="Weather.TurbidityEpisodes"/>).</summary>
		public float Turbidity;
		/// <summary>The prevailing wind's way (unit, xz), the way the swell runs.</summary>
		public float WayX, WayZ;
		/// <summary>The plume tide, 0.3..1 (<see cref="SceneGlowModes.PlumeTideOf"/>).</summary>
		public float PlumeTide;
		/// <summary>The scene's own vent's Strombolian burst, 0..1 (<see cref="Weather.VentBursts"/>, <c>_FishVentBurst</c>).</summary>
		public float VentBurst;

		/// <summary>A sky with nothing in it: a sun overhead, a full moon, no weather. What a glow reads before the client publishes.</summary>
		public static SceneGlowSky Calm => new SceneGlowSky { SunHeight = 1f, LocalTime01 = 0.5f, NightShare = 0.5f, MoonLight = 1f, Wet = 0.5f, WayX = 1f, PlumeTide = 0.3f };

		/// <summary>The four globals' vectors, as the GPU reads them (FishGlowModes.hlsl).</summary>
		public Vector4 A => new Vector4(SunHeight, LocalTime01, NightShare, Day);
		public Vector4 B => new Vector4(MoonLight, ParentLight, FieldTurn, OrbitTurn);
		public Vector4 C => new Vector4(Wet, Wind, Rain, Fog);
		public Vector4 D => new Vector4(Sea, Turbidity, WayX, WayZ);

		/// <summary>The tidal stress's rotating axis, <c>_FishTidalStress</c> (decision 90): (cos 2φ, sin 2φ, 0, 0), φ the orbit's turn.</summary>
		public Vector4 TidalStress
		{
			get
			{
				double a = 2.0 * SceneGlowModes.Turn * OrbitTurn;
				return new Vector4((float)Math.Cos(a), (float)Math.Sin(a), 0f, 0f);
			}
		}
	}

	/// <summary>
	/// The glow modes (<see cref="SceneGlowMode"/>), one definition read by the client's site lights (SiteLightPool) and, constant
	/// for constant, by the glowing parts and crystals on the GPU (FishGlowModes.hlsl), so a light and the stone that casts it move as
	/// one. Every rate on the motion clock is a whole number of cycles in its 10 000 s wrap (so a glow meets itself across it); the
	/// day-count modes read the local day number modulo <see cref="DayWrap"/>, which every count up to 16 divides.
	/// </summary>
	public static class SceneGlowModes
	{
		public const float Turn = 2f * Mathf.PI;

		/// <summary>The day number's wrap: the least common multiple of 1..16, so a count of up to 16 days never skips at it.</summary>
		public const int DayWrap = 720720;

		/// <summary>The most a day count may be (<see cref="DayWrap"/>), and the counts a mode uses when its entry names none.</summary>
		public const int MaxEvery = 16, StepEvery = 13, DayCountEvery = 8;

		/// <summary>How much of the day a day's step takes to arrive, from local midnight.</summary>
		public const float DayTurn = 0.01f;

		/// <summary>The caustic's three ripples: cycles a second and cycles a metre along x and z.</summary>
		public const float CausticAHz = 0.31f, CausticAX = 0.61f, CausticAZ = 0.23f;
		public const float CausticBHz = 0.47f, CausticBX = -0.29f, CausticBZ = 0.53f;
		public const float CausticCHz = 0.13f, CausticCX = 0.17f, CausticCZ = -0.41f;

		/// <summary>The breath: cycles a second (an eight-second swell) and its phase over the ground; how much fuller at dawn.</summary>
		public const float BreathHz = 0.125f, BreathX = 0.013f, BreathZ = 0.019f, DawnBoost = 0.4f, DawnWidth = 0.06f, DawnAfter = 0.02f;

		/// <summary>The crust's overturn: cycles a second (one every 400 s) and its phase over the ground.</summary>
		public const float OverturnHz = 0.0025f, OverturnX = 0.0071f, OverturnZ = 0.0053f;

		/// <summary>The swell: cycles a second (12.5 s) and its wavelength, metres (deep water: g·T²/2π).</summary>
		public const float SwellHz = 0.08f, SwellMetres = 244f;

		/// <summary>The trail: cycles a second (once each 1000 s) and how far a lap runs along its line, metres.</summary>
		public const float TrailHz = 0.001f, TrailMetres = 500f, TrailWidth = 10f, TrailX = 0.6f, TrailZ = 0.8f;

		/// <summary>The kneading: the crack bearing's field, cycles a metre along x and z.</summary>
		public const float CrackX = 0.0011f, CrackZ = 0.0007f;

		/// <summary>The tremor's tremble, cycles a second, and its phase over the ground.</summary>
		public const float TrembleHz = 6.1f, TrembleX = 0.21f, TrembleZ = 0.15f;

		/// <summary>The warm tint (decisions 43, 46, 69): firelight's colour, its luminance 1 (a warm glow is as bright as a cold one).</summary>
		public static readonly Vector3 WarmTint = new Vector3(1.4756f, 0.9149f, 0.4427f);

		/// <summary>The luminance weights (Rec. 709) the warm tint keeps.</summary>
		public static readonly Vector3 Luminance = new Vector3(0.2126f, 0.7152f, 0.0722f);

		/// <summary>
		/// Whether a mode carries its own daylight (sun, night, terminator, after sunset): it is shown as it is, never hidden by the
		/// dusk visibility every other glow shows through (<see cref="SceneGlow.Visibility"/>).
		/// </summary>
		public static bool OwnsDaylight(SceneGlowMode mode) => mode == SceneGlowMode.Sun || mode == SceneGlowMode.Night || mode == SceneGlowMode.Terminator
			|| mode == SceneGlowMode.AfterSunset;

		/// <summary>The source a glow emitter's light belongs to: a crystal's hue (0–3) is a crystal's, rune and faerie light a way glow's.</summary>
		public static SceneGlowSource SourceOf(SceneGlowHue hue) => hue == SceneGlowHue.Rune || hue == SceneGlowHue.Faerie ? SceneGlowSource.Way : SceneGlowSource.Crystal;

		/// <summary>The day count a mode runs to (1..<see cref="MaxEvery"/>): the entry's own, else the mode's.</summary>
		public static int EveryOf(SceneGlowMode mode, int every)
		{
			if (every <= 0)
			{
				every = mode == SceneGlowMode.DayCount ? DayCountEvery : StepEvery;
			}
			return every < 1 ? 1 : every > MaxEvery ? MaxEvery : every;
		}

		/// <summary>
		/// A glow's brightness under a mode at a moment of the shared motion clock (<paramref name="seconds"/>, wrapped at
		/// <see cref="SceneGlow.WrapSeconds"/>) for a glow at world (x, z). <see cref="SceneGlowMode.Default"/> reads as the pulse
		/// (a consumer with another default keeps its own). Before any dusk visibility (see <see cref="OwnsDaylight"/>).
		/// </summary>
		public static float Value(SceneGlowMode mode, int every, in Biomes.BiomeFountainEpisodes episodes, float seconds, float x, float z, in SceneGlowSky sky)
			=> Value(mode, every, in episodes, seconds, x, z, in sky, SceneGlowPlace.None);

		/// <summary>
		/// As <see cref="Value(SceneGlowMode, int, in Biomes.BiomeFountainEpisodes, float, float, float, in SceneGlowSky)"/>, for a glow
		/// whose stored surroundings are <paramref name="place"/> (2026-10-25, I6i-b; <see cref="SceneGlowPlaces.At"/>): a TRAIL creeps
		/// along its nearest stored line, a KNEADING reads that line's bearing, an EPISODE plays its nearest fountain vent's episodes.
		/// <see cref="SceneGlowPlace.None"/> is the overload above to the bit.
		/// </summary>
		public static float Value(SceneGlowMode mode, int every, in Biomes.BiomeFountainEpisodes episodes, float seconds, float x, float z, in SceneGlowSky sky,
			in SceneGlowPlace place)
		{
			float t = seconds;
			switch (mode)
			{
				case SceneGlowMode.Flicker:
					return SceneGlow.Flicker(t, x, z);
				case SceneGlowMode.Still:
					return 1f;
				case SceneGlowMode.Caustic:
				{
					float a = Sin(Fraction(CausticAHz * t + Fraction(x * CausticAX + z * CausticAZ)));
					float b = Sin(Fraction(CausticBHz * t + Fraction(x * CausticBX + z * CausticBZ)));
					float c = Sin(Fraction(CausticCHz * t + Fraction(x * CausticCX + z * CausticCZ)));
					float m = 1f - Math.Abs(a + b + c) / 3f;
					return 0.5f + 0.5f * m * m * m * m;
				}
				case SceneGlowMode.Step:
				{
					int n = EveryOf(mode, every);
					float from = StepLevel(sky.Day - 1f, n), to = StepLevel(sky.Day, n);
					return 0.2f + 0.8f * Lerp(from, to, Smooth(sky.LocalTime01 / DayTurn));
				}
				case SceneGlowMode.Sun:
					return 0.15f + 0.85f * Smooth((sky.SunHeight + 0.05f) / 0.55f);
				case SceneGlowMode.Night:
					return 0.15f + 0.85f * (1f - Smooth((sky.SunHeight + 0.05f) / 0.55f));
				case SceneGlowMode.Season:
					return 0.3f + 0.7f * Smooth(sky.Wet);
				case SceneGlowMode.Wind:
					return 0.25f + 0.75f * Smooth(sky.Wind);
				case SceneGlowMode.Breath:
				{
					float b = 0.5f - 0.5f * Cos(Fraction(BreathHz * t + Fraction(x * BreathX + z * BreathZ)));
					return (0.35f + 0.65f * Smooth(b)) * (1f + DawnBoost * Dawn(in sky));
				}
				case SceneGlowMode.Overturn:
				{
					float p = Fraction(OverturnHz * t + Fraction(x * OverturnX + z * OverturnZ));
					return 0.15f + 0.85f * Math.Max((float)Math.Exp(-6f * p), Smooth((p - 0.97f) / 0.03f));
				}
				case SceneGlowMode.AfterSunset:
					return AfterSunset(in sky);
				case SceneGlowMode.Swell:
				{
					float wave = 0.5f + 0.5f * Sin(Fraction(SwellHz * t - Fraction((x * sky.WayX + z * sky.WayZ) / SwellMetres)));
					return 1f - 0.6f * Lerp(0.25f, 1f, Clamp01(sky.Sea)) * (1f - wave);
				}
				case SceneGlowMode.Field:
				{
					float f = 0.5f + 0.5f * Cos(Fraction(2f * sky.FieldTurn));
					return 0.25f + 0.75f * f * f * f;
				}
				case SceneGlowMode.Terminator:
				{
					float h = sky.SunHeight / 0.09f;
					return 0.08f + 0.92f * (float)Math.Exp(-h * h);
				}
				case SceneGlowMode.Volvalight:
					return 0.15f + 0.85f * Clamp01(sky.ParentLight);
				case SceneGlowMode.Rain:
					return 0.25f + 0.75f * Smooth(sky.Rain * 2f);
				case SceneGlowMode.Trail:
				{
					// Along the nearest stored line (I6i-b), else along a fixed bearing (a scene that stores none).
					float along = place.OnLine ? place.Along : x * TrailX + z * TrailZ;
					float p = Fraction(TrailHz * t - Fraction(along / TrailMetres)) - 0.5f;
					return 0.15f + 0.85f * (float)Math.Exp(-(p * TrailWidth) * (p * TrailWidth));
				}
				case SceneGlowMode.NightLength:
					return 0.2f + 0.8f * Clamp01(sky.NightShare);
				case SceneGlowMode.Eyelid:
					return 0.04f + 0.96f * (1f - Smooth((sky.MoonLight - 0.04f) / 0.16f));
				case SceneGlowMode.DayCount:
				{
					int n = EveryOf(mode, every);
					float from = DayLit(sky.Day - 1f, n), to = DayLit(sky.Day, n);
					return 0.04f + 0.96f * Lerp(from, to, Smooth(sky.LocalTime01 / DayTurn));
				}
				case SceneGlowMode.Kneading:
				{
					Vector4 axis = sky.TidalStress;
					// The nearest stored crack's bearing (I6i-b), else the smooth field's.
					float cos2, sin2;
					if (place.OnLine)
					{
						cos2 = place.Cos2;
						sin2 = place.Sin2;
					}
					else
					{
						float bearing = Mathf.PI * (0.5f + 0.5f * Sin(Fraction(x * CrackX + z * CrackZ)));
						cos2 = (float)Math.Cos(2f * bearing);
						sin2 = (float)Math.Sin(2f * bearing);
					}
					float stress = 0.5f + 0.5f * (axis.x * cos2 + axis.y * sin2);
					return 0.2f + 0.8f * stress * stress;
				}
				case SceneGlowMode.Fog:
					return 0.3f + 0.7f * Smooth(sky.Fog * 1.5f);
				case SceneGlowMode.PlumeTide:
					return Clamp01(sky.PlumeTide);
				case SceneGlowMode.Tremor:
				{
					float tremble = 0.75f + 0.25f * Sin(Fraction(TrembleHz * t + Fraction(x * TrembleX + z * TrembleZ)));
					return 0.06f + 0.94f * Clamp01(sky.Turbidity) * tremble;
				}
				case SceneGlowMode.Episode:
					// The nearest stored fountain vent's own episodes (I6i-b), else the glow's cell's.
					return 0.12f + 0.88f * (place.AtVent ? Biomes.BiomeFountainEpisodes.At(in place.VentEpisodes, seconds, place.VentSeed)
						: Biomes.BiomeFountainEpisodes.GlowAt(in episodes, seconds, x, z));
				case SceneGlowMode.Burst:
					return 0.1f + 0.9f * Clamp01(sky.VentBurst);
				default:
					return SceneGlow.Pulse(t, x, z);
			}
		}

		/// <summary><see cref="Value(SceneGlowMode, int, in Biomes.BiomeFountainEpisodes, float, float, float, in SceneGlowSky)"/> of a biome's entry.</summary>
		public static float Value(in Biomes.BiomeGlowMode entry, float seconds, Vector3 at, in SceneGlowSky sky, SceneGlowPlace place = default)
			=> Value(entry.Mode, entry.Every, in entry.Episodes, seconds, at.x, at.z, in sky, in place);

		/// <summary>
		/// A glow emitter's light under a mode: <paramref name="intensity"/> × its brightness × how much shows (the dusk visibility, all
		/// of it underground; a mode that carries its own daylight shows as it is). Default (an entry naming only the warm tint) keeps the
		/// emitter's own wave: the flicker where its seed asks (<paramref name="flickers"/>), else the pulse.
		/// </summary>
		public static float Light(in Biomes.BiomeGlowMode entry, float intensity, float seconds, Vector3 at, in SceneGlowSky sky, float visibility, bool underground,
			bool flickers = false, SceneGlowPlace place = default)
		{
			if (entry.Mode == SceneGlowMode.Default)
			{
				return SceneGlow.Light(seconds, at, visibility, underground, flickers) * (intensity / SceneGlow.Intensity);
			}
			float shows = OwnsDaylight(entry.Mode) || underground ? 1f : Clamp01(visibility);
			return intensity * Value(in entry, seconds, at, in sky, place) * shows;
		}

		/// <summary>A colour warmed (decisions 43, 46, 69): its luminance in firelight's colour, or the colour itself when not warm.</summary>
		public static Color Warmed(Color colour, bool warm)
		{
			if (!warm)
			{
				return colour;
			}
			float l = colour.r * Luminance.x + colour.g * Luminance.y + colour.b * Luminance.z;
			return new Color(l * WarmTint.x, l * WarmTint.y, l * WarmTint.z, colour.a);
		}

		/// <summary>A step's level on a day: (day mod n + 1) / n.</summary>
		public static float StepLevel(float day, int n) => (PositiveMod(day, n) + 1f) / n;

		/// <summary>Whether a day is the lit one of a count of n (1 or 0).</summary>
		public static float DayLit(float day, int n) => PositiveMod(day, n) < 0.5f ? 1f : 0f;

		/// <summary>The dawn mist's bell (0..1): just after sunrise; none when the day has no sunrise (polar day or night).</summary>
		public static float Dawn(in SceneGlowSky sky)
		{
			float night = Clamp01(sky.NightShare), day = 1f - night;
			float sunrise = 0.5f - 0.5f * day + DawnAfter;
			float off = Fraction(sky.LocalTime01 - sunrise + 0.5f) - 0.5f;
			return Clamp01(1f - Math.Abs(off) / DawnWidth) * Smooth(Math.Min(night, day) / 0.02f);
		}

		/// <summary>The after-sunset glow (decision 58): 1 soon after sunset, fading toward 0.15 at dawn, dark by day.</summary>
		public static float AfterSunset(in SceneGlowSky sky)
		{
			float night = Clamp01(sky.NightShare);
			if (night <= 1e-4f)
			{
				return 0f;
			}
			float sunset = 0.5f + 0.5f * (1f - night);
			float progress = Fraction(sky.LocalTime01 - sunset) / night;
			if (progress >= 1f)
			{
				return 0f;
			}
			return (1f - 0.85f * progress) * Smooth(progress / 0.03f) * Smooth((1f - progress) / 0.03f);
		}

		// ── The sky's numbers, pure in the world clock (the client reads the celestial model for their inputs) ──

		/// <summary>The fallback period of the plume tide, the kneading and the field when a body has no orbit or host, hours (decision 53).</summary>
		public const double FallbackOrbitHours = 33.0, FallbackFieldHours = 10.0;

		/// <summary>Local days since the epoch: the solar days run less the local time (it turns at local midnight), modulo <see cref="DayWrap"/>.</summary>
		public static float DayNumber(double hours, double solarDayHours, double localTime01)
		{
			if (!(solarDayHours > 0.0) || double.IsInfinity(solarDayHours) || double.IsNaN(hours))
			{
				return 0f;
			}
			double day = Math.Round(hours / solarDayHours - localTime01);
			day -= Math.Floor(day / DayWrap) * DayWrap;
			return (float)day;
		}

		/// <summary>A turn of a period from an offset: frac(hours / period + offset°/360), in double. The fallback period for none.</summary>
		public static float TurnOf(double hours, double periodHours, double offsetDegrees, double fallbackHours)
		{
			double period = periodHours > 0.0 && !double.IsInfinity(periodHours) ? periodHours : fallbackHours;
			double turn = hours / period + offsetDegrees / 360.0;
			return (float)(turn - Math.Floor(turn));
		}

		/// <summary>The giant's field turning over a moon (synodic): frac(hours / host's rotation − hours / the moon's orbit).</summary>
		public static float FieldTurnOf(double hours, double hostRotationHours, double orbitHours)
		{
			double rotation = hostRotationHours > 0.0 ? hostRotationHours : FallbackFieldHours;
			double turn = hours / rotation - (orbitHours > 0.0 && !double.IsInfinity(orbitHours) ? hours / orbitHours : 0.0);
			return (float)(turn - Math.Floor(turn));
		}

		/// <summary>The plume tide (decision 53): 0.3 at pericentre, 1 at apocentre (half a turn), by the orbit's turn.</summary>
		public static float PlumeTideOf(float orbitTurn) => 0.3f + 0.7f * (0.5f - 0.5f * Cos(orbitTurn));

		/// <summary>The wet half-year (decision 59): the local summer (the season's sine, hemisphere by latitude), 0..1.</summary>
		public static float WetOf(float season01, float latitudeDegrees)
		{
			float summer = (float)Math.Sin(Turn * season01 - 0.5f * Mathf.PI) * (latitudeDegrees < 0f ? -1f : 1f);
			return 0.5f + 0.5f * summer;
		}

		/// <summary>
		/// A value eased between whole knots of a clock (<paramref name="knotSeconds"/> apart): linear between the knots either side of
		/// <paramref name="seconds"/>, each read by <paramref name="at"/> (its index). Pure when <paramref name="at"/> is.
		/// </summary>
		public static float KnotLerp(double seconds, double knotSeconds, Func<long, float> at)
		{
			double k = Math.Floor(seconds / knotSeconds);
			float f = (float)(seconds / knotSeconds - k);
			return Lerp(at((long)k), at((long)k + 1), f);
		}

		/// <summary>
		/// The mean over [seconds, seconds + window] of the line through whole knots (<see cref="KnotLerp"/>): continuous in time, and
		/// the same whatever was asked before. What "the rain of the next six hours" is (decision 75).
		/// </summary>
		public static float KnotMean(double seconds, double knotSeconds, double windowSeconds, Func<long, float> at)
		{
			double a = seconds / knotSeconds, b = (seconds + windowSeconds) / knotSeconds;
			long first = (long)Math.Floor(a), last = (long)Math.Floor(b);
			double sum = 0.0;
			for (long k = first; k <= last; k++)
			{
				double lo = Math.Max(a, k), hi = Math.Min(b, k + 1);
				if (hi <= lo)
				{
					continue;
				}
				float v0 = at(k), v1 = at(k + 1);
				// The integral of the line from v0 at k to v1 at k + 1, over [lo, hi].
				double u0 = lo - k, u1 = hi - k;
				sum += v0 * (u1 - u0) + (v1 - v0) * 0.5 * (u1 * u1 - u0 * u0);
			}
			return (float)(sum / Math.Max(1e-9, b - a));
		}

		// ── Names: every name a draft gave a mode (each resolves) ──

		private static readonly Dictionary<string, SceneGlowMode> names = BuildNames();

		private static Dictionary<string, SceneGlowMode> BuildNames()
		{
			var map = new Dictionary<string, SceneGlowMode>(StringComparer.OrdinalIgnoreCase);
			foreach (SceneGlowMode mode in Enum.GetValues(typeof(SceneGlowMode)))
			{
				map[mode.ToString()] = mode;
			}
			// The drafts' own words for them.
			map["Afterglow"] = SceneGlowMode.AfterSunset;
			map["After-sunset"] = SceneGlowMode.AfterSunset;
			map["Time since sunset"] = SceneGlowMode.AfterSunset;
			map["Sun-facing"] = SceneGlowMode.Sun;
			map["Steady"] = SceneGlowMode.Still;
			map["Caustic pulse"] = SceneGlowMode.Caustic;
			map["Daily step"] = SceneGlowMode.Step;
			map["Plume tide"] = SceneGlowMode.PlumeTide;
			map["Night length"] = SceneGlowMode.NightLength;
			map["Polar night"] = SceneGlowMode.NightLength;
			map["Day count"] = SceneGlowMode.DayCount;
			map["Turbidity"] = SceneGlowMode.Tremor;
			map["Fountain episode"] = SceneGlowMode.Episode;
			map["Twinkle"] = SceneGlowMode.Flicker;
			map["Strombolian"] = SceneGlowMode.Burst;
			map["Vent burst"] = SceneGlowMode.Burst;
			return map;
		}

		/// <summary>A mode by any name a draft gave it (case and spacing as written); false for a name no mode answers to.</summary>
		public static bool TryParse(string name, out SceneGlowMode mode)
		{
			mode = SceneGlowMode.Default;
			return !string.IsNullOrWhiteSpace(name) && names.TryGetValue(name.Trim(), out mode);
		}

		// ── Arithmetic, as the shader has it ──

		public static float Fraction(float v) => v - (float)Math.Floor(v);

		public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

		public static float Smooth(float t)
		{
			t = Clamp01(t);
			return t * t * (3f - 2f * t);
		}

		private static float Lerp(float a, float b, float t) => a + (b - a) * t;

		private static float Sin(float turns) => (float)Math.Sin(Turn * turns);

		private static float Cos(float turns) => (float)Math.Cos(Turn * turns);

		private static float PositiveMod(float v, int n)
		{
			float m = v - n * (float)Math.Floor(v / n);
			return m >= n ? m - n : m;
		}
	}
}
