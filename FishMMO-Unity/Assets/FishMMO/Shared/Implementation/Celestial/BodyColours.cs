using System;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;
using UnityEngine;

namespace FishMMO.Shared.Celestial
{
	/// <summary>
	/// The colours a world shows, worked out from what it is made of and how warm it is: its disc from space, the dust or
	/// droplets that colour its sky, its rings, and for a gas giant the bands of its cloud deck.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>From physics, not dice.</b> The generator used to roll a world's colour: a giant was tan or cyan on a coin flip,
	/// a rocky world some shade of rust, whatever its orbit or air. A world's colour is decided by what condenses out of
	/// its air at its temperature and what lies on its ground, and the same few rules hold across the solar system:
	/// </para>
	/// <list type="bullet">
	/// <item><b>Giants by their cloud tops' temperature</b> (Sudarsky's classes): below about 80 K methane takes the red
	/// out, blue-green to deep blue (Uranus, Neptune); 80–150 K ammonia clouds and their brown, ochre hazes in strong belts
	/// (Saturn, Jupiter); 150–350 K bright water-ice clouds, cream and white; 350–900 K no clouds at all, a deep azure;
	/// 900–1400 K sodium and potassium soak the light up, dark; hotter still, silicate clouds and a dull red glow.</item>
	/// <item><b>A thick air hides the ground</b>: sulphuric cloud makes a runaway greenhouse pale yellow (Venus), a cold
	/// thick air's tholin haze makes it orange (Titan), anything else under cloud is white.</item>
	/// <item><b>A thin air shows the ground through its dust</b>: dry and oxidised, rust (Mars); cold enough for nitrogen
	/// or methane to freeze out, the pinks and tans of tholins (Pluto).</item>
	/// <item><b>An airless body is its ground</b>: ice out past the frost line (Europa, Ganymede); sulphur where tides
	/// squeeze it molten (Io); grey regolith anywhere else, darker close in (the Moon, Mercury).</item>
	/// <item><b>An ordinary air</b> shows oceans, land and cloud in proportion to its water (Earth); frozen over, it is
	/// white; dry, it is a desert's tan.</item>
	/// </list>
	/// <para>
	/// A body's own seed varies each colour a little, so two worlds of a kind are not the same swatch. Pure: the inputs
	/// are a <see cref="Physics"/>, so the rules can be tested without assets.
	/// </para>
	/// </remarks>
	public static class BodyColours
	{
		/// <summary>What a body's colours are worked out from.</summary>
		public struct Physics
		{
			public WorldBodyKind Kind;
			public AtmosphereKind Atmosphere;
			/// <summary>0 dry … 1 ocean world.</summary>
			public float Water;
			/// <summary>The mean temperature of what is seen from space, kelvin: a giant's cloud tops, any other body's ground.</summary>
			public float Kelvin;
			/// <summary>Heat the tides make, 0 … 1 (Io about 1).</summary>
			public float TidalHeat;
			/// <summary>What its air condenses at its temperature.</summary>
			public Condensate Condensate;
			/// <summary>Rock stands molten on it.</summary>
			public bool Molten;
			public uint Seed;
		}

		/// <summary>A body's colours.</summary>
		public struct Look
		{
			/// <summary>The disc from space, all of it averaged: the body's <see cref="CelestialBody.Tint"/>.</summary>
			public Color Globe;
			/// <summary>What colours its sky: its dust, droplets or haze (<see cref="WorldBody.HazeColor"/>).</summary>
			public Color Haze;
			/// <summary>How much of the ground its air hides from space, 0 … 1.</summary>
			public float Veil;
			/// <summary>Its rings, if it has any: ice far from the star, dust and rock near it.</summary>
			public Color Rings;
			/// <summary>A giant's bands: its light zones, its dark belts, the deep it shows through, and its storms.</summary>
			public Color Zone, Belt, Deep, Storm;
			/// <summary>How strongly a giant's bands stand out from one another, 0 … 1 (Jupiter about 0.5, Uranus nearly 0).</summary>
			public float Banding;
		}

		/// <summary>The cloud-top temperature where a giant's clouds change, kelvin.</summary>
		public const float MethaneBelow = 80f, WaterAbove = 150f, CloudlessAbove = 350f, AlkaliAbove = 900f, SilicateAbove = 1400f;

		/// <summary>An airless body colder than this, kelvin, is covered in ice.</summary>
		public const float IcyBelow = 150f;

		/// <summary>A body this colder or below, kelvin, with water, is frozen over.</summary>
		public const float SnowballBelow = 240f;

		/// <summary>A thin air colder than this, kelvin, lies over ice and tholins rather than rust.</summary>
		public const float ColdThinBelow = 170f;

		/// <summary>Tidal heat (Io at 1) past which a moon's surface is sulphur from its volcanoes.</summary>
		public const float SulphurTides = 0.5f;

		public static Look For(in Physics body)
		{
			return body.Kind == WorldBodyKind.GasGiant ? Giant(body) : Solid(body);
		}

		// ── Giants ────────────────────────────────────────────────

		/// <summary>The giant's colour key, by its cloud tops' temperature: (kelvin, zone, belt, storm, banding).</summary>
		private static readonly (float k, Color zone, Color belt, Color storm, float banding)[] GiantKeys =
		{
			// Neptune: methane-blue, faint bands, dark storms.
			(45f, new Color(0.36f, 0.52f, 0.86f), new Color(0.24f, 0.36f, 0.72f), new Color(0.14f, 0.18f, 0.42f), 0.16f),
			// Uranus: pale cyan, almost featureless.
			(65f, new Color(0.70f, 0.88f, 0.92f), new Color(0.58f, 0.80f, 0.86f), new Color(0.88f, 0.95f, 0.96f), 0.06f),
			// Saturn: pale gold, soft bands.
			(95f, new Color(0.93f, 0.85f, 0.66f), new Color(0.80f, 0.68f, 0.48f), new Color(0.96f, 0.92f, 0.80f), 0.28f),
			// Jupiter: cream zones, red-brown belts, a red storm.
			(125f, new Color(0.92f, 0.86f, 0.74f), new Color(0.64f, 0.46f, 0.32f), new Color(0.78f, 0.38f, 0.26f), 0.55f),
			// Water clouds: bright cream and white.
			(180f, new Color(0.96f, 0.95f, 0.90f), new Color(0.84f, 0.82f, 0.74f), new Color(0.99f, 0.99f, 0.97f), 0.22f),
			(320f, new Color(0.95f, 0.95f, 0.94f), new Color(0.82f, 0.84f, 0.84f), new Color(1.00f, 1.00f, 1.00f), 0.18f),
			// Cloudless: the deep azure of the gas itself.
			(480f, new Color(0.38f, 0.54f, 0.82f), new Color(0.28f, 0.42f, 0.72f), new Color(0.52f, 0.66f, 0.88f), 0.10f),
			// Alkali: sodium and potassium soak up the light.
			(1100f, new Color(0.24f, 0.24f, 0.32f), new Color(0.16f, 0.15f, 0.22f), new Color(0.36f, 0.30f, 0.34f), 0.10f),
			// Silicate clouds over a hot, faintly glowing deep.
			(1700f, new Color(0.52f, 0.40f, 0.34f), new Color(0.38f, 0.24f, 0.20f), new Color(0.80f, 0.46f, 0.28f), 0.25f),
		};

		private static Look Giant(in Physics body)
		{
			float k = Mathf.Max(1f, body.Kelvin);
			int hi = 0;
			while (hi < GiantKeys.Length && GiantKeys[hi].k < k)
			{
				hi++;
			}
			Color zone, belt, storm;
			float banding;
			if (hi == 0 || hi == GiantKeys.Length)
			{
				var key = GiantKeys[hi == 0 ? 0 : GiantKeys.Length - 1];
				(zone, belt, storm, banding) = (key.zone, key.belt, key.storm, key.banding);
			}
			else
			{
				var a = GiantKeys[hi - 1];
				var b = GiantKeys[hi];
				// In log temperature: the classes are a few tens of kelvin apart when cold and hundreds when hot.
				float t = Mathf.InverseLerp(Mathf.Log(a.k), Mathf.Log(b.k), Mathf.Log(k));
				t = t * t * (3f - 2f * t);
				zone = Color.Lerp(a.zone, b.zone, t);
				belt = Color.Lerp(a.belt, b.belt, t);
				storm = Color.Lerp(a.storm, b.storm, t);
				banding = Mathf.Lerp(a.banding, b.banding, t);
			}
			// Its own: a little warmer or cooler, paler or deeper, more or less banded.
			float shift = Jitter(body.Seed, 1) * 0.035f, pale = Jitter(body.Seed, 2) * 0.05f;
			zone = Vary(zone, shift, pale);
			belt = Vary(belt, shift, pale * 0.5f);
			banding = Mathf.Clamp01(banding * (1f + Jitter(body.Seed, 3) * 0.3f));
			Color deep = belt * 0.55f;
			deep.a = 1f;
			// The disc, averaged: zones cover a little more than belts.
			Color globe = Color.Lerp(zone, belt, 0.4f);
			globe.a = 1f;
			return new Look
			{
				Globe = globe,
				Haze = Color.Lerp(Color.white, globe, 0.5f),
				Veil = 1f,
				Rings = RingColour(body),
				Zone = zone,
				Belt = belt,
				Deep = deep,
				Storm = storm,
				Banding = banding,
			};
		}

		// ── Everything with a surface ─────────────────────────────

		private static Look Solid(in Physics body)
		{
			float k = body.Kelvin;
			Color ground = Ground(body);
			Color globe, haze;
			float veil;
			switch (body.Atmosphere)
			{
				case AtmosphereKind.None:
					globe = ground;
					haze = Color.white;
					veil = 0f;
					break;
				case AtmosphereKind.Thick:
					if (body.Condensate == Condensate.SulphuricAcid || k > 400f)
					{
						// Sulphuric-acid cloud, top to bottom of the sky: pale yellow.
						haze = new Color(0.96f, 0.88f, 0.64f);
						globe = Vary(new Color(0.92f, 0.85f, 0.62f), Jitter(body.Seed, 4) * 0.03f, 0f);
					}
					else if (body.Condensate == Condensate.Methane || body.Condensate == Condensate.Nitrogen || body.Condensate == Condensate.Ammonia)
					{
						// Tholin haze, sunlight on methane making soot: orange.
						haze = new Color(0.92f, 0.62f, 0.30f);
						globe = Vary(new Color(0.86f, 0.58f, 0.28f), Jitter(body.Seed, 4) * 0.03f, 0f);
					}
					else
					{
						// Water cloud over everything: white, the ground's colour faint through the gaps.
						haze = Color.white;
						globe = Color.Lerp(new Color(0.90f, 0.91f, 0.92f), ground, 0.15f);
					}
					veil = 0.92f;
					break;
				case AtmosphereKind.Thin:
					if (body.Condensate == Condensate.Methane || body.Condensate == Condensate.Nitrogen || body.Condensate == Condensate.Ammonia
						|| k < ColdThinBelow)
					{
						// Ices of ammonia, methane and nitrogen, reddened by the sun into tholins: pinks and tans. Too cold for
						// the iron in the dust to rust as a warmer thin air's does.
						ground = Vary(new Color(0.84f, 0.70f, 0.58f), Jitter(body.Seed, 5) * 0.04f, 0f);
						haze = new Color(0.86f, 0.74f, 0.64f);
					}
					else if (body.Water < 0.15f)
					{
						// Dry and dusty: the dust is the ground's own colour, lifted into the air.
						haze = Color.Lerp(Color.white, ground, 0.8f);
					}
					else
					{
						haze = Color.Lerp(Color.white, ground, 0.35f);
					}
					globe = ground;
					veil = 0.06f;
					break;
				default:
					// An ordinary air: oceans, land and cloud as its water says; frozen over, white.
					if (k < SnowballBelow && body.Water > 0.1f)
					{
						globe = new Color(0.88f, 0.90f, 0.95f);
						haze = Color.white;
					}
					else
					{
						var ocean = new Color(0.08f, 0.24f, 0.62f);
						Color land = k > 320f || body.Water < 0.15f ? ground : Color.Lerp(ground, new Color(0.36f, 0.46f, 0.26f), Mathf.Clamp01(body.Water * 1.4f));
						float sea = Mathf.Clamp01(body.Water);
						Color surface = Color.Lerp(land, ocean, sea);
						// Cloud over about a third to two thirds of it, by how much water there is to make it.
						float cloud = Mathf.Lerp(0.1f, 0.32f, sea);
						globe = Color.Lerp(surface, new Color(0.95f, 0.96f, 0.98f), cloud);
						haze = body.Water < 0.15f ? Color.Lerp(Color.white, ground, 0.7f) : Color.white;
					}
					veil = Mathf.Lerp(0.15f, 0.45f, Mathf.Clamp01(body.Water));
					break;
			}
			globe.a = haze.a = 1f;
			return new Look
			{
				Globe = globe,
				Haze = haze,
				Veil = veil,
				Rings = RingColour(body),
				Zone = globe,
				Belt = globe,
				Deep = globe,
				Storm = globe,
				Banding = 0f,
			};
		}

		/// <summary>The bare ground: ice, sulphur, rust or grey regolith.</summary>
		private static Color Ground(in Physics body)
		{
			float k = body.Kelvin;
			if (body.TidalHeat >= SulphurTides && body.Atmosphere == AtmosphereKind.None)
			{
				// Sulphur frosts and flows from the volcanoes the tides keep going: yellow, orange and white, the lava
				// only in dark spots (Io), so it wins over the molten rock under it.
				return Vary(new Color(0.86f, 0.76f, 0.38f), Jitter(body.Seed, 6) * 0.04f, 0f);
			}
			if (body.Molten)
			{
				// Fresh lava cools to black glass and dark basalt.
				return Vary(new Color(0.30f, 0.26f, 0.24f), Jitter(body.Seed, 6) * 0.03f, 0f);
			}
			if (k < IcyBelow && body.Atmosphere == AtmosphereKind.None)
			{
				// Water ice, dirtied by meteoric dust: off-white to beige-grey.
				return Vary(new Color(0.84f, 0.82f, 0.78f), Jitter(body.Seed, 6) * 0.03f, Jitter(body.Seed, 7) * 0.08f);
			}
			if (body.Atmosphere != AtmosphereKind.None && body.Water < 0.15f)
			{
				// Air with oxygen in it and no water to wash it: iron oxide, rust.
				return Vary(new Color(0.72f, 0.42f, 0.26f), Jitter(body.Seed, 6) * 0.04f, Jitter(body.Seed, 7) * 0.06f);
			}
			// Bare rock and regolith; darker close to the star, where space weathering is hardest.
			float dark = Mathf.InverseLerp(250f, 600f, k) * 0.12f;
			return Vary(new Color(0.58f - dark, 0.55f - dark, 0.51f - dark), Jitter(body.Seed, 6) * 0.02f, Jitter(body.Seed, 7) * 0.06f);
		}

		/// <summary>Rings: water ice where it is cold enough to keep it, dark dust and rock where it is not.</summary>
		private static Color RingColour(in Physics body)
		{
			return body.Kelvin < 200f
				? Vary(new Color(0.90f, 0.86f, 0.78f), Jitter(body.Seed, 8) * 0.03f, 0f)
				: Vary(new Color(0.46f, 0.42f, 0.38f), Jitter(body.Seed, 8) * 0.03f, 0f);
		}

		// ── From a body ───────────────────────────────────────────

		/// <summary>
		/// The temperature of a giant's cloud tops, kelvin: its starlight's equilibrium at a giant's albedo (about 0.34,
		/// as Jupiter's) with the heat it still makes for itself added (about 100 K's worth, Jupiter's 125 K against
		/// its 110 K from sunlight alone). Not <see cref="ClimateModel.MeanSurfaceKelvin(SolarSystemProfile, WorldBody)"/>,
		/// which for a giant is its habitable layer's (<see cref="CloudWorld"/>).
		/// </summary>
		public static float CloudTopKelvin(SolarSystemProfile system, WorldBody body)
		{
			double insolation = ClimateModel.MeanInsolation(system, body);
			double equilibrium = ClimateModel.SolarConstantTemperature * Math.Pow(Math.Max(0.0, insolation), 0.25) * Math.Pow(1.0 - 0.34, 0.25);
			const double Internal = 100.0;
			return (float)Math.Pow(Math.Pow(equilibrium, 4.0) + Math.Pow(Internal, 4.0), 0.25);
		}

		/// <summary>The physics of a body in its system.</summary>
		public static Physics PhysicsOf(SolarSystemProfile system, WorldBody body)
		{
			if (body == null)
			{
				return default;
			}
			bool giant = body.Kind == WorldBodyKind.GasGiant;
			var physics = new Physics
			{
				Kind = body.Kind,
				Atmosphere = body.Atmosphere,
				Water = body.Water,
				Seed = body.ResolvedTerrainSeed,
			};
			if (giant)
			{
				physics.Kelvin = CloudTopKelvin(system, body);
				physics.Condensate = AirPhysics.CondensateFor(physics.Kelvin);
				return physics;
			}
			BiomeWorldConditions conditions = BiomeWorldConditions.For(system, body);
			physics.Kelvin = (float)ClimateModel.MeanSurfaceKelvin(system, body);
			physics.TidalHeat = conditions.TidalHeat;
			physics.Condensate = conditions.Condensate;
			physics.Molten = conditions.HasMoltenRock;
			return physics;
		}

		/// <summary>A body's colours in its system.</summary>
		public static Look Of(SolarSystemProfile system, WorldBody body) => For(PhysicsOf(system, body));

		/// <summary>
		/// Gives a body its colours: its tint, its sky's haze colour and its rings'. The amounts (how much haze, how
		/// opaque the rings) are left as they were rolled.
		/// </summary>
		public static void Apply(SolarSystemProfile system, WorldBody body)
		{
			if (body == null)
			{
				return;
			}
			Look look = Of(system, body);
			body.Tint = look.Globe;
			if (body.Atmosphere != AtmosphereKind.None)
			{
				body.HazeColor = look.Haze;
			}
			if (body.Rings != null)
			{
				body.Rings.Tint = look.Rings;
			}
		}

		// ── Helpers ───────────────────────────────────────────────

		/// <summary>−1 … 1 from a seed and a salt.</summary>
		private static float Jitter(uint seed, uint salt)
		{
			unchecked
			{
				uint h = seed ^ (salt * 0x9E3779B9u);
				h ^= h >> 16;
				h *= 0x7FEB352Du;
				h ^= h >> 15;
				h *= 0x846CA68Bu;
				h ^= h >> 16;
				return (h & 0xFFFFFF) / 8388607.5f - 1f;
			}
		}

		/// <summary>A colour turned a little warmer (positive) or cooler, and paler (positive) or deeper.</summary>
		private static Color Vary(Color c, float warmth, float pale)
		{
			var v = new Color(c.r + warmth, c.g, c.b - warmth, 1f);
			v = pale >= 0f ? Color.Lerp(v, Color.white, pale) : v * (1f + pale);
			v.r = Mathf.Clamp01(v.r);
			v.g = Mathf.Clamp01(v.g);
			v.b = Mathf.Clamp01(v.b);
			v.a = 1f;
			return v;
		}
	}
}
