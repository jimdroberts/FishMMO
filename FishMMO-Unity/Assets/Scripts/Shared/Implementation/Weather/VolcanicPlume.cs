using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// What a volcanic vent puts into the sky: a buoyant eruption column and its umbrella cloud where
	/// there is air, a ballistic fountain where there is none — and where what it throws up comes down.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>With air</b> the column is a buoyant plume. It rises to a height set by how much it erupts —
	/// the fourth root of the mass eruption rate (Morton, Taylor and Turner; Wilson and Walker's
	/// H = 0.236 Q^¼ km on our own world) — leans over in the wind, and spreads out as an umbrella at
	/// the height its mixture is as light as the air round it, about three quarters of the top
	/// (Sparks). Ash falls out of the umbrella beneath it and downwind, thinning exponentially away from
	/// the vent as real deposits do (Pyle), over the distance a grain drifts while it falls from the
	/// umbrella: wind × height ÷ settling speed. Without air there is nothing to be buoyant in and
	/// nothing to hold a grain up: none of this happens, and the ash weather is gated on air.
	/// </para>
	/// <para>
	/// <b>Without air</b> (an Io) a vent is a fountain: every grain and frost flake is thrown on a
	/// ballistic arc with no drag, rises v²/2g, and lands where its arc lands — in a ring round the
	/// vent, the far edge at v²·sin 2α / g for the widest launch angle α, with no drift because there is
	/// no wind. That is the vent's own effect and never weather. Io's Prometheus-class plumes are
	/// launched at about half a kilometre a second (roughly 100 km tall); a scene a few kilometres
	/// across shows them at <see cref="PlayableScale"/> of that speed, so they stand hundreds of metres
	/// to a kilometre or so tall and land inside the scene.
	/// </para>
	/// <para>
	/// Pure functions of the world, the wind and the vent: the server's weather, the client's weather
	/// and the client's drawing all ask these same functions, so the ash falls out of the plume that
	/// is drawn.
	/// </para>
	/// </remarks>
	public static class VolcanicPlume
	{
		/// <summary>m per (kg/s)^¼: Wilson and Walker's 0.236 km, on our own world.</summary>
		public const float TopCoefficient = 236f;
		/// <summary>The umbrella spreads at this share of the column's top: where the mixture is neutrally buoyant.</summary>
		public const float UmbrellaShare = 0.75f;
		/// <summary>The wind at the umbrella against the wind near the ground: the log profile and the shear above it.</summary>
		public const float WindAloft = 1.5f;
		/// <summary>How fast the umbrella's edge spreads sideways as it is carried downwind, per metre downwind.</summary>
		public const float SpreadPerMetre = 0.2f;
		/// <summary>Below this wind the umbrella spreads round the vent, not down a wind.</summary>
		public const float CalmWind = 0.5f;
		/// <summary>The mass eruption rate of a vent at emission 0 and 1, as powers of ten of kg/s: a degassing vent to a sub-Plinian eruption.</summary>
		public const float QuietRateExponent = 3f, FullRateExponent = 7.5f;

		/// <summary>Io's plumes' launch speed at emission 0 and 1, m/s: Prometheus-class to Pele-class.</summary>
		public const float QuietLaunchSpeed = 500f, FullLaunchSpeed = 1000f;
		/// <summary>The share of a real launch speed a scene shows: heights go as its square.</summary>
		public const float PlayableScale = 0.1f;
		/// <summary>How far from vertical the widest grains of a fountain are thrown, degrees: a hollow cone.</summary>
		public const float FountainConeDegrees = 25f;
		/// <summary>The narrowest launch angle that still lands in the ring, as a share of the widest.</summary>
		public const float FountainInnerShare = 0.55f;

		/// <summary>One plume: where it rises, how high, which way it leans and what falls out of it.</summary>
		public struct Plume
		{
			/// <summary>The vent, world XZ.</summary>
			public Vector2 Vent;
			/// <summary>How hard it erupts, 0..1 (the ground's emission, boosted by an eruption).</summary>
			public float Emission;
			/// <summary>What it throws up.</summary>
			public WeatherSubstance Substance;
			/// <summary>True for an eruption cell's, false for a vent's steady plume.</summary>
			public bool FromCell;
			/// <summary>A stable number for the plume's look.</summary>
			public uint Seed;

			/// <summary>The column's top above the vent, m.</summary>
			public float Top;
			/// <summary>The umbrella's height above the vent, m.</summary>
			public float Umbrella;
			/// <summary>The umbrella's half-width at the column, m.</summary>
			public float Radius;
			/// <summary>Where the column's top stands from the vent, XZ m: its lean downwind.</summary>
			public Vector2 TopOffset;
			/// <summary>The wind at the umbrella, XZ m/s.</summary>
			public Vector2 Wind;
			/// <summary>How fast a typical grain of it settles, m/s.</summary>
			public float FallSpeed;

			/// <summary>False for a default (no plume).</summary>
			public bool Valid => Top > 0f;
		}

		/// <summary>Whether a world has the air for a buoyant column: the same air any storm needs.</summary>
		public static bool CanRise(in PlanetAir planet) => StormPhysics.CanForm(planet);

		/// <summary>The mass eruption rate for an emission, kg/s: a degassing vent at 0, a sub-Plinian eruption at 1.</summary>
		public static float MassRate(float emission01)
		{
			return Mathf.Pow(10f, Mathf.Lerp(QuietRateExponent, FullRateExponent, Mathf.Clamp01(emission01)));
		}

		/// <summary>
		/// The column's top, m above the vent: H = 0.236 km · Q^¼ on our world, × √(g⊕/g) × ρ^−¼.
		/// </summary>
		/// <remarks>
		/// The buoyancy flux goes as g·Q/ρ and the air's stratification frequency as g, and a plume
		/// rises as F^¼ N^−¾ — so a world with weaker gravity raises a taller column from the same
		/// eruption, and thinner air a slightly taller one.
		/// </remarks>
		public static float TopMetres(float massRate, float gravity, float airRelative)
		{
			float g = Mathf.Max(0.05f, gravity);
			float rho = Mathf.Max(1e-3f, airRelative);
			float top = TopCoefficient * Mathf.Pow(Mathf.Max(0f, massRate), 0.25f)
				* Mathf.Sqrt(FishMMO.Shared.Celestial.SurfacePhysics.EarthGravity / g) * Mathf.Pow(rho, -0.25f);
			return Mathf.Clamp(top, 50f, 60000f);
		}

		/// <summary>How fast the column's mixture climbs on average, m/s: a fraction of √(g·H).</summary>
		public static float RiseSpeed(float top, float gravity) => 0.25f * Mathf.Sqrt(Mathf.Max(0.05f, gravity) * Mathf.Max(1f, top));

		/// <summary>
		/// How far downwind the column's top stands from the vent: it is carried sideways by the wind
		/// while it climbs, H·U/(U + w), never more than twice its height (a bent-over plume).
		/// </summary>
		public static Vector2 TopOffset(float top, float gravity, Vector2 windAloft)
		{
			float u = windAloft.magnitude;
			if (u < 1e-4f)
			{
				return Vector2.zero;
			}
			float lean = Mathf.Min(2f * top, top * u / (u + RiseSpeed(top, gravity)));
			return windAloft / u * lean;
		}

		/// <summary>How fast a grain settles in still air, m/s: Stokes for the fine, the drag law for the coarse, whichever is slower.</summary>
		public static float GrainFallSpeed(float grainMetres, float grainDensity, float gravity, float airDensity)
		{
			float d = Mathf.Max(1e-7f, grainMetres);
			float g = Mathf.Max(0f, gravity);
			float stokes = g * d * d * Mathf.Max(0f, grainDensity) / (18f * 1.8e-5f);
			float drag = Mathf.Sqrt(3.1f * g * d * Mathf.Max(0f, grainDensity) / Mathf.Max(1e-3f, airDensity));
			return Mathf.Max(0.01f, Mathf.Min(stokes, drag));
		}

		/// <summary>A vent's plume on a world, in the wind near the ground there. Invalid where there is no air.</summary>
		public static Plume Of(Vector2 vent, float emission01, WeatherSubstance substance, in PlanetAir planet, Vector2 surfaceWind, bool fromCell = false, uint seed = 0)
		{
			if (!CanRise(planet) || emission01 <= 0f)
			{
				return default;
			}
			float top = TopMetres(MassRate(emission01), planet.Gravity, planet.AirRelative);
			Vector2 wind = surfaceWind * WindAloft;
			float grain = substance != null ? substance.GrainMetres : 1e-4f;
			float density = substance != null ? substance.GrainDensity : 2400f;
			return new Plume
			{
				Vent = vent,
				Emission = Mathf.Clamp01(emission01),
				Substance = substance,
				FromCell = fromCell,
				Seed = seed,
				Top = top,
				Umbrella = UmbrellaShare * top,
				Radius = 0.5f * UmbrellaShare * top + 100f,
				TopOffset = TopOffset(top, planet.Gravity, wind),
				Wind = wind,
				FallSpeed = GrainFallSpeed(grain, density, planet.Gravity, FishMMO.Shared.Celestial.SurfacePhysics.EarthAirDensity * planet.AirRelative),
			};
		}

		/// <summary>How far downwind a plume's fall thins by 1/e, m: the drift of a typical grain falling from the umbrella.</summary>
		public static float FalloutLength(in Plume plume)
		{
			return Mathf.Max(plume.Radius, plume.Wind.magnitude * plume.Umbrella / Mathf.Max(0.01f, plume.FallSpeed));
		}

		/// <summary>
		/// How hard a plume's ash falls at a point, 0..1 (the precipitation channel): under the umbrella
		/// and downwind of it, none upwind past the umbrella's edge.
		/// </summary>
		/// <remarks>
		/// Along the wind from the column's top: the umbrella's own spread upwind of it, and an exponential
		/// thinning downwind over <see cref="FalloutLength"/>. Across it: a Gaussian as wide as the
		/// umbrella has spread by then, and thinner for being wider — the same ash over more ground.
		/// In calm air, a disc round the vent as wide as the umbrella.
		/// </remarks>
		public static float FalloutAt(in Plume plume, Vector2 position)
		{
			if (!plume.Valid)
			{
				return 0f;
			}
			Vector2 fromTop = position - (plume.Vent + plume.TopOffset);
			float u = plume.Wind.magnitude;
			float r0 = Mathf.Max(1f, plume.Radius);
			if (u < CalmWind)
			{
				return plume.Emission * Mathf.Exp(-fromTop.sqrMagnitude / (r0 * r0));
			}
			Vector2 down = plume.Wind / u;
			float x = Vector2.Dot(fromTop, down);
			float y = Vector2.Dot(fromTop, new Vector2(-down.y, down.x));
			float along = x >= 0f ? Mathf.Exp(-x / FalloutLength(plume)) : Mathf.Exp(-(x * x) / (r0 * r0));
			float width = r0 + SpreadPerMetre * Mathf.Max(0f, x);
			float across = Mathf.Exp(-(y * y) / (width * width));
			return plume.Emission * along * across * (r0 / width);
		}

		// ── Without air: the ballistic fountain ─────────────────────────

		/// <summary>A vent's launch speed in a scene, m/s: Io's half to one kilometre a second, at <see cref="PlayableScale"/>.</summary>
		public static float LaunchSpeed(float emission01)
		{
			return PlayableScale * Mathf.Lerp(QuietLaunchSpeed, FullLaunchSpeed, Mathf.Sqrt(Mathf.Clamp01(emission01)));
		}

		/// <summary>How high a grain launched straight up climbs with no drag, m: v²/2g.</summary>
		public static float FountainHeight(float launchSpeed, float gravity)
		{
			return launchSpeed * launchSpeed / (2f * Mathf.Max(0.01f, gravity));
		}

		/// <summary>Where a grain launched at an angle from the vertical lands, m from the vent: v²·sin 2α / g.</summary>
		public static float BallisticRange(float launchSpeed, float degreesFromVertical, float gravity)
		{
			return launchSpeed * launchSpeed * Mathf.Sin(2f * degreesFromVertical * Mathf.Deg2Rad) / Mathf.Max(0.01f, gravity);
		}

		/// <summary>How long a grain launched at an angle from the vertical is in flight, s: 2v·cos α / g.</summary>
		public static float FlightSeconds(float launchSpeed, float degreesFromVertical, float gravity)
		{
			return 2f * launchSpeed * Mathf.Cos(degreesFromVertical * Mathf.Deg2Rad) / Mathf.Max(0.01f, gravity);
		}

		/// <summary>
		/// The ring a fountain's fallout lands in, m from the vent: from the narrowest launch that
		/// reaches it to the widest. No wind to drift it, so it is centred on the vent.
		/// </summary>
		public static void FalloutRing(float launchSpeed, float gravity, out float inner, out float outer)
		{
			inner = BallisticRange(launchSpeed, FountainInnerShare * FountainConeDegrees, gravity);
			outer = BallisticRange(launchSpeed, FountainConeDegrees, gravity);
		}

		/// <summary>Whether a vent on this world is a fountain: no air to be buoyant in, and gravity to bring it down.</summary>
		public static bool IsFountain(in PlanetAir planet, float gravity) => !planet.HasAir && gravity > 0f;
	}
}
