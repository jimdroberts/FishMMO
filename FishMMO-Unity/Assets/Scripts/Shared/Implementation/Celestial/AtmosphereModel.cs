using System;
using UnityEngine;

namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// What a sky looks like, worked out from the air it is made of: how much there is, how much dust
	/// hangs in it, and how high the sun stands.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A sky's colours used to be painted: ten gradients to a profile, by the sun's altitude, the same
	/// for every world that used it. They were painted well — the noon zenith in the Temperate Sky has
	/// red, green and blue in the ratio 0.23 : 0.49 : 1, and sunlight scattered once through one
	/// thickness of our own air comes out 0.25 : 0.46 : 1 — which is to say that what was painted was
	/// the physics, by eye. So the physics can stand in for the painting, and then a world with thin
	/// air, or thick, or full of dust, has the sky that air would really give it.
	/// </para>
	/// <para>
	/// The model is single scattering with its corners knocked off, in three wavelengths:
	/// </para>
	/// <list type="bullet">
	/// <item>Air scatters blue far more than red (Rayleigh: as the inverse fourth power of wavelength);
	/// dust and haze scatter all colours about alike (Mie), and have a colour of their own.</item>
	/// <item>Looking up, the path through the air is short and what comes back is in proportion to what
	/// the air scatters: blue. Looking along the horizon the path is dozens of times longer, every
	/// colour is scattered to saturation, and the sky goes pale.</item>
	/// <item>Whatever lights that air has come through air itself, and the lower the sun the more of
	/// it: the blue goes first, so a low sun is orange and lights the horizon orange.</item>
	/// </list>
	/// <para>
	/// The corners: light that has been scattered more than once is whiter than light scattered once,
	/// so what lights the air is part-way greyed; and the zenith, the horizon and the sun's own disc
	/// each see the sun through a different effective depth of air, because the air overhead is lit
	/// from well above the ground and a real sunset never reddens as far as thirty-eight thicknesses
	/// of air would make it. Those depths, the greying and the two overall brightnesses are the
	/// model's only free numbers. They were FITTED, by search, to the Temperate Sky's own gradients at
	/// ten sun altitudes, so that a standard atmosphere under a sun-like star gives the sky this game
	/// already had: noon and mid-morning to within a few hundredths, sunset's orange horizon and deep
	/// zenith in character, and deep night exactly.
	/// </para>
	/// <para>
	/// The rest of a sample — fog, the ground below the horizon, the three ambient colours, the
	/// clouds' lit and shadowed sides — were never independent of the sky in the painted gradients
	/// either. Each is a fixed mix of the zenith, the horizon and the horizon's brightness, found by
	/// least squares from the same asset (to 0.02–0.08 rms), so a derived sky brings a consistent fog
	/// and ambient with it.
	/// </para>
	/// <para>
	/// The star's own colour is NOT an input. The sky system already recolours a sample by every sun
	/// that is up, weighted by brightness and height, which a model of one sun could not do; this
	/// gives the sky of a white star and leaves that to it.
	/// </para>
	/// </remarks>
	public static class AtmosphereModel
	{
		// One thickness of standard air, straight up: scattering coefficient at the ground times the
		// height it thins out over. Red, green, blue.
		private static readonly Vector3 Rayleigh = new Vector3(5.8e-6f * 8000f, 13.5e-6f * 8000f, 33.1e-6f * 8000f);
		private const float Mie = 21e-6f * 1200f;

		// Fitted to the Temperate Sky (see the remarks). Depths are in thicknesses of air.
		private const float ZenithSunDepth = 3.947f;
		private const float HorizonSunDepth = 9.378f;
		private const float DiscSunDepth = 8.502f;
		private const float HorizonViewDepth = 16.033f;
		private const float ZenithGain = 4.232f;
		private const float HorizonGain = 1.163f;
		private const float ZenithGreying = 0.8f;
		private const float HorizonGreying = 0.249f;
		private const float ZenithDuskPower = 3f;
		private const float HorizonDuskPower = 2.656f;
		private const float SunsetGlow = 1.103f;

		// What is left when the sun is eighteen degrees down: airglow and starlight.
		private static readonly Color NightZenith = new Color(0.01f, 0.012f, 0.03f);
		private static readonly Color NightHorizon = new Color(0.02f, 0.025f, 0.05f);

		/// <summary>How much air, against our own at 1.</summary>
		public static float Density(AtmosphereKind air)
		{
			switch (air)
			{
				case AtmosphereKind.None: return 0f;
				case AtmosphereKind.Thin: return 0.15f;
				case AtmosphereKind.Thick: return 3f;
				default: return 1f;
			}
		}

		/// <summary>
		/// How many thicknesses of air the light crosses coming in at this height above the horizon
		/// (Kasten and Young): one from overhead, about thirty-eight along the horizon. Held there for
		/// a sun below it, where the dusk terms take over.
		/// </summary>
		public static float AirMass(float altitudeDegrees)
		{
			float h = Mathf.Max(0f, altitudeDegrees);
			return 1f / (Mathf.Sin(h * Mathf.Deg2Rad) + 0.50572f * Mathf.Pow(h + 6.07995f, -1.6364f));
		}

		private static float Luminance(Vector3 c) => 0.2126f * c.x + 0.7152f * c.y + 0.0722f * c.z;

		private static Vector3 Through(Vector3 depth, float thicknesses)
		{
			return new Vector3(Mathf.Exp(-depth.x * thicknesses), Mathf.Exp(-depth.y * thicknesses), Mathf.Exp(-depth.z * thicknesses));
		}

		private static Vector3 Greyed(Vector3 c, float amount)
		{
			float grey = Luminance(c);
			return new Vector3(Mathf.Lerp(c.x, grey, amount), Mathf.Lerp(c.y, grey, amount), Mathf.Lerp(c.z, grey, amount));
		}

		/// <summary>Untouched up to 0.8, then rolling off so that nothing quite reaches 1.</summary>
		private static float Shoulder(float value)
		{
			return value < 0.8f ? value : 0.8f + 0.2f * (1f - Mathf.Exp(-(value - 0.8f) / 0.2f));
		}

		private static Color ToColor(Vector3 c) => new Color(Mathf.Max(0f, c.x), Mathf.Max(0f, c.y), Mathf.Max(0f, c.z), 1f);

		/// <summary>
		/// The sky's colours and light for this air and this sun altitude. The moon's light is not the
		/// air's to decide and is left at zero for the caller to fill.
		/// </summary>
		/// <param name="haze">How much dust and haze hangs in the air, against a clear day's at 1.</param>
		/// <param name="hazeColor">What colour that dust is: white for water haze, tan for a desert world's.</param>
		public static SkySample Evaluate(AtmosphereKind air, float haze, Color hazeColor, float sunAltitude)
		{
			float density = Density(air);
			if (density <= 0f)
			{
				// No air: nothing to scatter. A black sky with the stars out at noon, and a sun that is
				// the same hard white from the horizon to overhead, there or not there.
				return new SkySample
				{
					Zenith = new Color(0.003f, 0.003f, 0.006f),
					Horizon = new Color(0.006f, 0.006f, 0.01f),
					Ground = new Color(0.004f, 0.004f, 0.005f),
					Fog = new Color(0.006f, 0.006f, 0.01f),
					AmbientSky = new Color(0.02f, 0.02f, 0.03f),
					AmbientEquator = new Color(0.015f, 0.015f, 0.02f),
					AmbientGround = new Color(0.01f, 0.01f, 0.012f),
					SunLight = Color.white,
					SunIntensity = sunAltitude > -1f ? 1.4f : 0f,
					CloudLit = Color.black,
					CloudShadow = Color.black,
					StarVisibility = 1f,
				};
			}

			// The dust is NOT in proportion to the gas. How much of it hangs in the air is the world's
			// business — its deserts, its volcanoes, its winds — and a world with a hundredth of our
			// air can carry several times our dust, which is exactly why such a world's sky is
			// butterscotch and not a thin dark blue: there is too little gas to scatter blue and
			// plenty of dust to scatter tan. Scaled by the gas, thin air had no dust to speak of and
			// every thin-aired world came out blue. Only the last of it goes as the air thins to
			// nothing, since something has to hold it up.
			float dust = Mathf.Max(0f, haze) * Mie * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(density));
			var depth = new Vector3(Rayleigh.x * density + dust, Rayleigh.y * density + dust, Rayleigh.z * density + dust);
			// What colour the scattered light is: the air scatters white light (its blue is in HOW MUCH
			// it scatters, not in a tint), the dust scatters its own colour.
			var scattered = new Vector3(
				(Rayleigh.x * density + dust * hazeColor.r) / Mathf.Max(1e-9f, depth.x),
				(Rayleigh.y * density + dust * hazeColor.g) / Mathf.Max(1e-9f, depth.y),
				(Rayleigh.z * density + dust * hazeColor.b) / Mathf.Max(1e-9f, depth.z));

			float airMass = AirMass(sunAltitude);
			// Below the horizon the air is still lit for a while, from further and further round the world.
			float dusk = sunAltitude < 0f ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-18f, 0f, sunAltitude)) : 1f;
			// How much of a sunset it is: all of one with the sun on the horizon, none by 25° up, and
			// fading with the dusk below.
			float low = sunAltitude > -6f
				? 1f - Mathf.SmoothStep(0f, 1f, Mathf.Abs(sunAltitude) / 25f)
				: Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-18f, -6f, sunAltitude)) * (1f - Mathf.SmoothStep(0f, 1f, 6f / 25f));

			Vector3 litZenith = Greyed(Through(depth, Mathf.Min(airMass, ZenithSunDepth)), ZenithGreying);
			Vector3 litHorizon = Greyed(Through(depth, Mathf.Min(airMass, HorizonSunDepth)), HorizonGreying);
			float zenithDusk = Mathf.Pow(dusk, ZenithDuskPower), horizonDusk = Mathf.Pow(dusk, HorizonDuskPower) * (1f + SunsetGlow * low);
			var zenith = new Vector3(
				ZenithGain * (1f - Mathf.Exp(-depth.x)) * litZenith.x * scattered.x * zenithDusk + NightZenith.r,
				ZenithGain * (1f - Mathf.Exp(-depth.y)) * litZenith.y * scattered.y * zenithDusk + NightZenith.g,
				ZenithGain * (1f - Mathf.Exp(-depth.z)) * litZenith.z * scattered.z * zenithDusk + NightZenith.b);
			var horizon = new Vector3(
				HorizonGain * (1f - Mathf.Exp(-depth.x * HorizonViewDepth)) * litHorizon.x * scattered.x * horizonDusk + NightHorizon.r,
				HorizonGain * (1f - Mathf.Exp(-depth.y * HorizonViewDepth)) * litHorizon.y * scattered.y * horizonDusk + NightHorizon.g,
				HorizonGain * (1f - Mathf.Exp(-depth.z * HorizonViewDepth)) * litHorizon.z * scattered.z * horizonDusk + NightHorizon.b);

			// Deep air is lit mostly by light that has been scattered many times, and that light is
			// white: a thick atmosphere's zenith is pale, not a deeper blue. And no sky is brighter
			// than what lights it. Scattering once, taken at its word, gave thick air a zenith of
			// one and a half; the shoulder starts at 0.8, above anything standard air reaches but at
			// the very top of a clear noon, so the home world's sky is not moved by it.
			zenith = Greyed(zenith, Mathf.Clamp01((density - 1f) / 2.5f) * 0.6f);
			zenith = new Vector3(Shoulder(zenith.x), Shoulder(zenith.y), Shoulder(zenith.z));
			horizon = new Vector3(Shoulder(horizon.x), Shoulder(horizon.y), Shoulder(horizon.z));

			// The sun's own light: what gets through, as a colour, a little whitened — a light is never
			// as red as the disc looks — and how much of it there is as a strength. Against the
			// strength of an overhead sun in standard air, softened (eyes and cameras both are), so a
			// clear noon is the 1.3 it always was; thick air dims it at every height and thin brightens it.
			Vector3 disc = Through(depth, Mathf.Min(airMass, DiscSunDepth));
			float brightest = Mathf.Max(disc.x, Mathf.Max(disc.y, disc.z));
			Vector3 sunColour = brightest > 1e-6f ? disc / brightest : new Vector3(1f, 0.35f, 0.15f);
			sunColour = Vector3.Lerp(sunColour, Vector3.one, 0.3f);
			float standardNoon = Luminance(Through(new Vector3(Rayleigh.x + Mie, Rayleigh.y + Mie, Rayleigh.z + Mie), 1f));
			float arriving = Luminance(Through(depth, airMass)) / Mathf.Max(1e-6f, standardNoon);
			float sunIntensity = 1.3f * Mathf.Pow(Mathf.Max(0f, arriving), 0.45f) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-6f, 0f, sunAltitude));

			float horizonLight = Luminance(horizon);
			var grey = new Vector3(horizonLight, horizonLight, horizonLight);
			Vector3 sunlit = sunColour * (sunIntensity / 1.3f);

			// Stars: out once the sun is well down, as ever — and out in daylight too where the air is
			// too thin to hide them, which is how bright the sky overhead is and nothing else.
			float byNight = Mathf.Clamp01(Mathf.InverseLerp(-2f, -14f, sunAltitude));
			float throughDay = 1f - Mathf.Clamp01(Luminance(zenith) / 0.12f);

			return new SkySample
			{
				Zenith = ToColor(zenith),
				Horizon = ToColor(horizon),
				// Each a fixed mix of the sky's own colours: see the remarks.
				Ground = ToColor(grey * 0.361f + horizon * 0.063f),
				Fog = ToColor(horizon * 0.476f + grey * 0.433f),
				AmbientSky = ToColor(zenith * 0.309f + horizon * 0.156f + grey * 0.430f),
				AmbientEquator = ToColor(horizon * 0.268f + grey * 0.289f),
				AmbientGround = ToColor(grey * 0.175f + horizon * 0.058f),
				SunLight = ToColor(sunColour),
				SunIntensity = sunIntensity,
				CloudLit = ToColor(sunlit * 0.140f + horizon * 1.059f + zenith * 0.039f),
				CloudShadow = ToColor(zenith * 0.164f + horizon * 0.170f + grey * 0.415f),
				StarVisibility = Mathf.Max(byNight, throughDay),
			};
		}
	}
}
