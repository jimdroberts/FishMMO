using UnityEngine;
using FishMMO.Shared.Biomes;

namespace FishMMO.Shared.Weather
{
	/// <summary>What kind of storm a cell is: a physical event, not a named weather.</summary>
	/// <remarks>
	/// Each is something the air does under conditions it can be seen to meet — a thunderstorm where
	/// a parcel lifted to its base keeps rising on its own, a supercell where that happens in strong
	/// wind, a hurricane over warm water in weak wind well off the equator. The director spawns them
	/// where the air allows, and a cell does to the air under it what that kind of storm does; what
	/// falls, how hard, and whether it thunders is then worked out from that air like anywhere else.
	/// </remarks>
	public enum StormKind : byte
	{
		/// <summary>A convective cell: a tower, a downpour, lightning.</summary>
		Thunderstorm = 0,
		/// <summary>A rotating storm in strong wind shear, with a tornado hanging from it.</summary>
		Supercell = 1,
		/// <summary>A line of storms along a gust front.</summary>
		SquallLine = 2,
		/// <summary>A hurricane: warm sea, weak shear and the spin of the world.</summary>
		TropicalCyclone = 3,
		/// <summary>A storm's cold outflow running over dry loose ground and lifting it as a wall.</summary>
		Haboob = 4,
		/// <summary>A sunlit whirl over hot, dry, loose ground.</summary>
		DustDevil = 5,
		/// <summary>The ground itself: a vent pouring what it emits into the air.</summary>
		Eruption = 6,
	}

	/// <summary>What the ground under the air gives it: what the wind can lift, and what it puts out on its own.</summary>
	public struct GroundTraits
	{
		/// <summary>Loose material the wind lifts once it blows hard enough: sand, dust, regolith. Null for rock, soil, ice and water.</summary>
		public WeatherSubstance Loose;
		/// <summary>What the ground emits by itself — a volcano's ash, a geyser field's ice. Null for nearly everywhere.</summary>
		public WeatherSubstance Emits;
		/// <summary>How hard it emits, 0..1.</summary>
		public float EmissionRate;
		/// <summary>Whether this is open water, which is what a hurricane feeds on and where nothing blows up off the ground.</summary>
		public bool Water;
		/// <summary>
		/// Whether what it emits comes out of vents (<see cref="WeatherSubstance.Vented"/>): then it
		/// falls out of the vents' plumes, downwind (<see cref="VolcanicVents"/>), not here.
		/// </summary>
		public bool Vented;

		/// <summary>The traits of a biome's ground, and whether the point is under the water line.</summary>
		public static GroundTraits Of(BiomeTemplate biome, bool underWater)
		{
			if (underWater)
			{
				return new GroundTraits { Water = true };
			}
			// A vapour (a geyser basin's steam) is not weather: it rises off its springs and vents and
			// evaporates into the air (GeothermalVents, SteamPhysics). Counted here it would fall as a
			// grain and raise volcanic eruptions over a field of hot springs.
			if (biome != null && biome.Emits != null && biome.Emits.Vapour)
			{
				return new GroundTraits { Loose = biome.LooseGround };
			}
			return biome == null ? default : new GroundTraits
			{
				Loose = biome.LooseGround,
				Emits = biome.Emits,
				EmissionRate = Mathf.Clamp01(biome.EmissionRate),
				Vented = biome.Emits != null && biome.Emits.Vented,
			};
		}
	}

	/// <summary>
	/// What storms do to the air, where the air makes them, and how big they are.
	/// </summary>
	public static class StormPhysics
	{
		/// <summary>Every kind, for commands and tools.</summary>
		public static readonly StormKind[] Kinds =
		{
			StormKind.Thunderstorm, StormKind.Supercell, StormKind.SquallLine, StormKind.TropicalCyclone,
			StormKind.Haboob, StormKind.DustDevil, StormKind.Eruption,
		};

		/// <summary>How the kind lies on the ground.</summary>
		public static StormCellShape ShapeOf(StormKind kind)
		{
			switch (kind)
			{
				case StormKind.SquallLine:
				case StormKind.Haboob:
					return StormCellShape.Front;
				case StormKind.TropicalCyclone:
					return StormCellShape.Eyewall;
				case StormKind.Supercell:
				case StormKind.DustDevil:
					return StormCellShape.Funnel;
				default:
					return StormCellShape.Disc;
			}
		}

		/// <summary>The kind's name as people say it.</summary>
		public static string NameOf(StormKind kind)
		{
			switch (kind)
			{
				case StormKind.Supercell: return "Supercell";
				case StormKind.SquallLine: return "Squall line";
				case StormKind.TropicalCyclone: return "Tropical cyclone";
				case StormKind.Haboob: return "Haboob";
				case StormKind.DustDevil: return "Dust devil";
				case StormKind.Eruption: return "Eruption";
				default: return "Thunderstorm";
			}
		}

		/// <summary>Reads a kind from a name or a common word for it ("tornado", "hurricane", "sandstorm").</summary>
		public static bool TryParse(string text, out StormKind kind)
		{
			kind = StormKind.Thunderstorm;
			if (string.IsNullOrWhiteSpace(text))
			{
				return false;
			}
			string t = text.Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
			switch (t)
			{
				case "thunderstorm": case "storm": case "thunder": kind = StormKind.Thunderstorm; return true;
				case "supercell": case "tornado": kind = StormKind.Supercell; return true;
				case "squallline": case "squall": case "front": kind = StormKind.SquallLine; return true;
				case "tropicalcyclone": case "hurricane": case "typhoon": case "cyclone": kind = StormKind.TropicalCyclone; return true;
				case "haboob": case "sandstorm": case "duststorm": kind = StormKind.Haboob; return true;
				case "dustdevil": case "whirlwind": kind = StormKind.DustDevil; return true;
				case "eruption": case "volcano": case "geyser": kind = StormKind.Eruption; return true;
			}
			return false;
		}

		/// <summary>
		/// The air under a storm: the same air, with what the storm does to it added in proportion
		/// to how much of the storm is here.
		/// </summary>
		/// <remarks>
		/// A storm is converging, rising, condensing air — so under it the pressure falls, the air
		/// is damper and far less stable, a tower stands, and the wind rises. How much of each is the
		/// kind's: a hurricane's pressure falls further than any thunderstorm's; a haboob is the
		/// storm's cold outflow, so it is the wind and not the rain; a dust devil is only wind.
		/// </remarks>
		public static WeatherDriver.Synoptic Perturb(in WeatherDriver.Synoptic air, StormKind kind, float influence)
		{
			float w = Mathf.Clamp01(influence);
			if (w <= 0f)
			{
				return air;
			}
			float humidity, instability, pressure, tower, wind;
			switch (kind)
			{
				case StormKind.Supercell:       humidity = 0.30f; instability = 0.55f; pressure = -0.6f; tower = 1f; wind = 45f; break;
				case StormKind.SquallLine:      humidity = 0.35f; instability = 0.40f; pressure = -0.35f; tower = 1f; wind = 14f; break;
				case StormKind.TropicalCyclone: humidity = 0.45f; instability = 0.30f; pressure = -1.2f; tower = 0.8f; wind = 38f; break;
				case StormKind.Haboob:          humidity = -0.05f; instability = 0.15f; pressure = 0.1f; tower = 0.25f; wind = 18f; break;
				case StormKind.DustDevil:       humidity = 0f; instability = 0.1f; pressure = -0.05f; tower = 0f; wind = 14f; break;
				case StormKind.Eruption:        humidity = 0.1f; instability = 0.25f; pressure = -0.1f; tower = 0.8f; wind = 4f; break;
				default:                        humidity = 0.35f; instability = 0.35f; pressure = -0.35f; tower = 1f; wind = 8f; break;
			}
			WeatherDriver.Synoptic local = air;
			local.Humidity = Mathf.Clamp01(air.Humidity + humidity * w);
			local.Instability = Mathf.Clamp01(air.Instability + instability * w);
			local.Pressure = Mathf.Clamp(air.Pressure + pressure * w, -1f, 1f);
			local.Tower = Mathf.Max(air.Tower, tower * w);
			local.ColumnType = Mathf.Clamp01(WeatherDriver.BaseColumnType(local.Instability) + local.Tower * WeatherDriver.TowerGain(local.Instability));
			float speed = air.Wind.magnitude;
			Vector2 direction = speed > 1e-4f ? air.Wind / speed : Vector2.up;
			local.Wind = direction * (speed + wind * w);
			if (kind == StormKind.Supercell)
			{
				local.Mesocyclone = Mathf.Max(air.Mesocyclone, w);
			}
			return local;
		}

		/// <summary>
		/// Whether a world has the air for a storm of any kind at all: something to lift, to carry
		/// what falls and to hold it up.
		/// </summary>
		/// <remarks>
		/// An airless body has none, so every kind is refused there — an eruption included. What a
		/// vent throws up on a world with no air flies on a ballistic arc and lands round the vent
		/// (Io's plumes); it is the vent's own effect and never weather, which is air carrying it.
		/// A storm cell asked for by name is refused rather than spawned and drawn as nothing: a cell
		/// that exists but does nothing is still a slot, a timeline entry and a spawn event.
		/// </remarks>
		public static bool CanForm(in PlanetAir planet) => planet.HasAir && planet.Gravity > 0f;

		/// <summary>
		/// The weather at the heart of a storm of this kind, in the air of a place: what that storm
		/// does to that air, at full strength, and what the air then does.
		/// </summary>
		public static WeatherFrame PeakFrame(StormKind kind, in WeatherSample around, out WeatherSubstance substance)
		{
			substance = null;
			if (!CanForm(around.Planet))
			{
				return WeatherFrame.Clear;
			}
			WeatherDriver.Synoptic air = Perturb(around.OpenAir, kind, 1f);
			AirColumn column = AirColumn.Of(around.Planet, around.OpenColumn.SurfaceKelvin, air.Humidity, air.Pressure, air.Instability);
			WeatherFrame frame = WeatherPhysics.Frame(air, column, around.Planet, around.Ground, around.Temperature, EmissionBoost(kind, 1f), out substance);
			WeatherField.StormsInHeavyRain(ref frame, around.Temperature);
			return frame;
		}

		/// <summary>How much harder the ground emits under this storm: an eruption is the ground going off.</summary>
		public static float EmissionBoost(StormKind kind, float influence) => kind == StormKind.Eruption ? 1f + 30f * Mathf.Clamp01(influence) : 1f;

		/// <summary>How likely each kind of storm is to form, per square kilometre per director pass.</summary>
		public struct Likelihood
		{
			public float Thunderstorm, Supercell, SquallLine, TropicalCyclone, Haboob, DustDevil, Eruption;

			public float Total => Thunderstorm + Supercell + SquallLine + TropicalCyclone + Haboob + DustDevil + Eruption;

			public float Of(StormKind kind)
			{
				switch (kind)
				{
					case StormKind.Supercell: return Supercell;
					case StormKind.SquallLine: return SquallLine;
					case StormKind.TropicalCyclone: return TropicalCyclone;
					case StormKind.Haboob: return Haboob;
					case StormKind.DustDevil: return DustDevil;
					case StormKind.Eruption: return Eruption;
					default: return Thunderstorm;
				}
			}

			/// <summary>A kind chosen in proportion to how likely each is, from a number in 0..1.</summary>
			public StormKind Pick(float random01)
			{
				float total = Total;
				if (total <= 0f)
				{
					return StormKind.Thunderstorm;
				}
				float at = Mathf.Clamp01(random01) * total;
				foreach (StormKind kind in Kinds)
				{
					at -= Of(kind);
					if (at <= 0f)
					{
						return kind;
					}
				}
				return StormKind.Thunderstorm;
			}
		}

		/// <summary>
		/// What the air over a place makes likely: a storm forms where the air meets its conditions,
		/// and nowhere else.
		/// </summary>
		/// <remarks>
		/// <list type="bullet">
		/// <item>A thunderstorm needs a parcel that, lifted to its cloud base, keeps rising on its own
		/// below the lid — free convection — with the energy to build a tower (CAPE), and water to
		/// build it from.</item>
		/// <item>A supercell is the same storm in strong wind, which tilts the updraught away from its
		/// own rain and sets it turning, and only with a lot of energy.</item>
		/// <item>A squall line is storms strung along the gust front a low drives.</item>
		/// <item>A tropical cyclone needs open water above about 26 °C, weak wind to keep it upright,
		/// and the spin of the world — none within five degrees of the equator, where there is none
		/// to spin it — and it wants a low to start from.</item>
		/// <item>A haboob is a storm's outflow over dry loose ground; a dust devil is loose ground under
		/// a hot, dry, calm midday; an eruption is ground that emits.</item>
		/// </list>
		/// </remarks>
		public static Likelihood LikelihoodAt(in WeatherDriver.Synoptic air, in AirColumn column, in GroundTraits ground, float latitudeDegrees)
		{
			var result = new Likelihood();
			// No energy is no storm: CAPE is zero wherever a lifted parcel never rises on its own.
			float energy = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(100f, 1500f, column.Cape));
			float moisture = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 0.75f, air.Humidity));
			float wind = air.Wind.magnitude;
			float dry = 1f - column.RelativeHumidity;
			float loose = ground.Loose != null ? 1f : 0f;

			result.Thunderstorm = energy * moisture;
			result.Supercell = result.Thunderstorm * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1200f, 2800f, column.Cape))
				* Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(10f, 18f, wind)) * 0.4f;
			result.SquallLine = result.Thunderstorm * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(8f, 16f, wind))
				* Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.1f, -0.5f, air.Pressure)) * 0.6f;
			float latitude = Mathf.Abs(latitudeDegrees);
			float spun = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(4f, 8f, latitude)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(25f, 35f, latitude)));
			result.TropicalCyclone = (ground.Water ? 1f : 0f) * spun
				* Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(297.5f, 301.5f, column.SurfaceKelvin))
				* (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(8f, 14f, wind)))
				* moisture * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.3f, -0.8f, air.Pressure)) * 0.15f;
			result.Haboob = energy * loose * dry * 0.5f;
			float midday = Mathf.Clamp01(1f - Mathf.Abs(air.LocalTime01 - 0.56f) / 0.14f);
			result.DustDevil = loose * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(298f, 310f, column.SurfaceKelvin))
				* (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3f, 8f, wind))) * Mathf.Clamp01(dry * 1.5f - 0.5f) * midday * 0.4f;
			result.Eruption = ground.Emits != null ? ground.EmissionRate * 0.1f : 0f;
			return result;
		}

		/// <summary>
		/// How deep a storm's cold outflow runs along the ground, m: the height of a haboob's wall.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The outflow is air the storm's rain chilled as it fell through the dry layer under the
		/// cloud, which sank and spreads over the ground as a pool colder than the air round it. The
		/// pool is as deep as the chilled air feeding it, some three fifths of the dry layer it fell
		/// through: over a desert, whose cloud bases stand two to four kilometres up, one to two and
		/// a half kilometres — the height of the dust walls over Khartoum and Phoenix.
		/// </para>
		/// <para>
		/// Held between a tenth and three tenths of the air's scale height. A humid storm's pool is
		/// not as shallow as its low base alone would make it — its downdraught drags cold air down
		/// from inside the cloud as well — and none is deeper than the lower air it runs through. On
		/// a world whose air stands taller the bases, the pools and the walls all stand taller with it.
		/// </para>
		/// </remarks>
		public static float OutflowDepth(in AirColumn column)
		{
			float scale = Mathf.Max(1f, column.ScaleHeight);
			return Mathf.Clamp(0.6f * column.Base, 0.1f * scale, 0.3f * scale);
		}

		/// <summary>
		/// How fast a storm's cold outflow runs out along the ground, m/s: the speed of a haboob's wall.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A pool of air colder than its surroundings spreads as a density current, at about
		/// 0.8·√(g′h) for a depth h, where g′ = g·Δθ/θ is the pull of its extra weight (Benjamin;
		/// Simpson). Its chill Δθ is what the rain did to it: kept saturated by the rain evaporating
		/// into it, the downdraught warms at the moist lapse on its way down from the base while the
		/// dry air it sinks through is laid out at the dry lapse, so it arrives colder by the gap
		/// between the two over the depth of the dry layer — five to twelve kelvin, as measured under
		/// haboobs, and held to that.
		/// </para>
		/// <para>
		/// The column carries the air's scale height, R·T/g, and not g itself, so g/θ is taken as R/H
		/// with R our own air's: exact for an air of nitrogen and oxygen on any world, within a quarter
		/// in speed for one of carbon dioxide. Fifteen to twenty metres a second over our own deserts,
		/// as haboobs run; slower on a world that pulls less.
		/// </para>
		/// </remarks>
		public static float OutflowSpeed(in AirColumn column, float depthMeters)
		{
			float chill = Mathf.Clamp((column.DryLapse - column.MoistLapse) * column.Base, 2f, 12f);
			// g/θ = R/H, with R our own air's 287.05 J/(kg·K).
			float pull = 287.05f * chill / Mathf.Max(1f, column.ScaleHeight);
			return 0.8f * Mathf.Sqrt(Mathf.Max(0f, pull * depthMeters));
		}

		/// <summary>
		/// How big a storm of this kind is here, and how long it lasts, from the air it grows in.
		/// </summary>
		/// <remarks>
		/// A deeper storm is a wider one — a cell's width runs with the depth it convects through —
		/// and a storm lives a few turnovers of its own updraught. Kept to the scale a scene holds:
		/// the director spawns inside the scene and a cell wider than the scene is a sky of one
		/// weather, so the widths stay those the scenes were built round.
		/// </remarks>
		public static void Dimensions(StormKind kind, in AirColumn column, float random01a, float random01b,
			out float radiusMeters, out float extentMeters, out float lifetimeSeconds)
		{
			float depth = Mathf.Max(500f, column.TowerCeiling - column.Base);
			float updraft = Mathf.Max(5f, column.Updraft);
			float size = 0.75f + 0.5f * Mathf.Clamp01(random01a);
			float life = 0.75f + 0.5f * Mathf.Clamp01(random01b);
			float turnover = depth / updraft;
			switch (kind)
			{
				case StormKind.Supercell:
					// The cell is the tornado: its core, 50 m in a weak one to 250 m in a violent wedge,
					// and how far its winds are felt. The storm it hangs from is the anatomy's
					// (StormAnatomy): a supercell's rain and hail fall over its forward flank, well away
					// from the tornado, and a cell as wide as the storm spread tornado winds over it all.
					radiusMeters = Mathf.Lerp(50f, 250f, Mathf.InverseLerp(1200f, 3500f, column.Cape)) * size;
					extentMeters = Mathf.Clamp(0.03f * depth, 300f, 800f) * size;
					lifetimeSeconds = Mathf.Clamp(0.5f * turnover, 180f, 900f) * life;
					break;
				case StormKind.SquallLine:
					// A line of storms tens of kilometres long, its rain band a few kilometres deep behind
					// the gust front: it was under half a kilometre deep and four long, a shower in a row.
					radiusMeters = Mathf.Clamp(0.25f * depth, 2500f, 8000f) * size;
					extentMeters = Mathf.Clamp(1.5f * depth, 12000f, 45000f) * size;
					lifetimeSeconds = Mathf.Clamp(4f * turnover, 720f, 2700f) * life;
					break;
				case StormKind.TropicalCyclone:
					radiusMeters = 2000f * size;
					extentMeters = 350f * size;
					lifetimeSeconds = 2700f * life;
					break;
				case StormKind.Haboob:
				{
					/* A haboob is a storm's cold outflow running out over loose ground, and the dust it
					 * lifts is that outflow made visible: a wall as tall as the cold pool is deep,
					 * running at the pool's own speed (OutflowDepth, OutflowSpeed). Its line is the
					 * pool's edge, which has been spreading for the twenty minutes or so a storm's
					 * outflow takes to organise before anyone sees a wall — tens of kilometres of it,
					 * far wider than a scene, and that is the point of one: it comes across the whole
					 * horizon at once. Its depth across is its head and the gusty wake behind it, about
					 * twice the wall's height. And it lives as long as it runs: a density current keeps
					 * its speed through its slumping phase for several times the width of the pool that
					 * released it, and a storm's pool is about ten times as wide as it is deep — some
					 * eighty depths in all, an hour and a half to three at the speeds they run, as the
					 * Sudan's haboobs last. It was 215 m deep and 1.2 km long: a hedge of dust, not a
					 * wall. */
					float wall = OutflowDepth(column);
					float speed = Mathf.Max(3f, OutflowSpeed(column, wall));
					radiusMeters = 2f * wall * size;
					extentMeters = Mathf.Clamp(speed * 1200f, 8000f, 30000f) * size;
					lifetimeSeconds = Mathf.Clamp(80f * wall / speed, 2400f, 12600f) * life;
					break;
				}
				case StormKind.DustDevil:
					radiusMeters = 8f * size;
					extentMeters = 130f * size;
					lifetimeSeconds = 390f * life;
					break;
				case StormKind.Eruption:
					radiusMeters = 450f * size;
					extentMeters = 0f;
					lifetimeSeconds = 1800f * life;
					break;
				default:
					// A thunderstorm's rain area: a few kilometres across under a tower several wide.
					radiusMeters = Mathf.Clamp(0.08f * depth, 1500f, 4000f) * size;
					extentMeters = 0f;
					lifetimeSeconds = Mathf.Clamp(3f * turnover, 480f, 3600f) * life;
					break;
			}
		}
	}

	/// <summary>
	/// The weather each kind of storm makes at its heart, in the air around one place: worked out
	/// once per kind and kept.
	/// </summary>
	/// <remarks>
	/// For everything that draws many points of many storms around one place — the weather map, the
	/// ground's cover map, the lightning schedule, the far rain curtains. A storm does to the air under
	/// it what its kind does; this is that air's weather at full strength, and each point then takes
	/// the share of it the cell's shape gives it there, as it used to take a preset's.
	/// </remarks>
	public sealed class StormFrames
	{
		private WeatherSample around;
		private readonly WeatherFrame[] frames = new WeatherFrame[8];
		private readonly WeatherSubstance[] substances = new WeatherSubstance[8];
		private int known;

		public StormFrames() { }

		public StormFrames(in WeatherSample around) => Reset(around);

		/// <summary>The weather of the place the storms are worked out around: the air each one grows in.</summary>
		public WeatherSample Around => around;

		/// <summary>Starts again around a different place.</summary>
		public void Reset(in WeatherSample sample)
		{
			around = sample;
			known = 0;
		}

		/// <summary>The weather at the heart of a storm of this kind, here.</summary>
		public WeatherFrame Of(StormKind kind) => Of(kind, out _);

		/// <summary>The same, and what is falling in it.</summary>
		public WeatherFrame Of(StormKind kind, out WeatherSubstance substance)
		{
			int index = Mathf.Clamp((int)kind, 0, frames.Length - 1);
			if ((known & (1 << index)) == 0)
			{
				frames[index] = StormPhysics.PeakFrame(kind, around, out substances[index]);
				known |= 1 << index;
			}
			substance = substances[index];
			return frames[index];
		}
	}

	/// <summary>
	/// The weather an air makes: the one function everything that falls, blows, clouds over or
	/// thunders is worked out from.
	/// </summary>
	public static class WeatherPhysics
	{
		/// <summary>
		/// The weather of an air over a ground, on a world.
		/// </summary>
		/// <param name="temperatureScale">The place's temperature on the climate scale, for what falls as what.</param>
		/// <param name="emissionBoost">How much harder the ground is emitting (an eruption over it).</param>
		public static WeatherFrame Frame(in WeatherDriver.Synoptic air, in AirColumn column, in PlanetAir planet, in GroundTraits ground,
			float temperatureScale, float emissionBoost, out WeatherSubstance substance)
		{
			return Frame(air, column, planet, ground, temperatureScale, emissionBoost, 0f, null, out substance);
		}

		/// <summary>
		/// The weather of an air over a ground, on a world, with the ash falling out of the plumes
		/// overhead or upwind (<see cref="VolcanicVents.FalloutAt"/>).
		/// </summary>
		/// <param name="fallout">How hard the plumes' fallout comes down here, 0..1.</param>
		/// <param name="falloutSubstance">What it is; null for plain ash.</param>
		public static WeatherFrame Frame(in WeatherDriver.Synoptic air, in AirColumn column, in PlanetAir planet, in GroundTraits ground,
			float temperatureScale, float emissionBoost, float fallout, WeatherSubstance falloutSubstance, out WeatherSubstance substance)
		{
			var accumulator = new WeatherAccumulator();

			// The water cycle: cloud, rain, hail, fog, lightning and wind from the air.
			WeatherFrame weather = WeatherDriver.Background(air);
			// Air too dry to condense before the lid stops it rising has no low cloud to show.
			weather[WeatherChannel.CloudCover] *= column.LowCloudAllowed;
			weather[WeatherChannel.CloudBase] = Mathf.Clamp01(column.Base / 4000f);
			// Sinking air under a high dries whatever it sinks through: rain needs a cloud a good
			// way deeper than its own base, and under a low lid there is not the room. Measured in
			// the air's own scale heights, so a world whose sky stands taller needs a taller room.
			float room = (Mathf.Min(column.Cap, column.Tropopause) - column.Base) / Mathf.Max(1f, column.ScaleHeight);
			weather[WeatherChannel.Precipitation] *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.06f, 0.3f, room));
			// And rain falls out of cloud. The background's rain is the sky's as a whole, so under a
			// broken sky it is a shower's worth spread over ground that is mostly in sunshine: a
			// quarter covered is dry where you stand, the showers are the storm cells' to bring, and
			// only a sky closing over rains everywhere. Without this a sky of scattered cumulus
			// drizzled on every square metre and hazed over its own sun.
			weather[WeatherChannel.Precipitation] *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.75f, weather[WeatherChannel.CloudCover]));
			// What falls as hail is the updraught's to say, not the background's.
			if (weather[WeatherChannel.Precipitation] > 0f)
			{
				float hail = HailShare(air, column, planet);
				weather[WeatherChannel.RainWeight] = 1f - hail;
				weather[WeatherChannel.HailWeight] = hail;
			}
			else
			{
				weather[WeatherChannel.RainWeight] = 0f;
				weather[WeatherChannel.HailWeight] = 0f;
			}
			// The fog is a layer on the ground with a top, not the same haze at every height: as deep as
			// the night has chilled the air through its dew-point spread, stirred deeper by the wind
			// against the stable air, capped by the cloud base and the lid, and lifted off the ground
			// into stratus once the wind is turbulent right through it or the morning sun warms the
			// ground under it (FogLayer). Its depth and its lift ride in the fog channels.
			FogLayer.Of(air, column, planet, weather[WeatherChannel.FogDensity], weather[WeatherChannel.CloudCover]).WriteTo(ref weather);
			// Any condensate but water is typed here, by its own freezing point, before anything
			// else joins it: the background comes out of the driver as "rain", and nitrogen below
			// 63 K or methane below 91 K falls as snow — it was drawn as rain streaks tinted like
			// snow, and wetted the ground. Water is typed at the end, by the scale's freezing band.
			if (planet.Condensate != Condensate.Water)
			{
				TypeCondensate(ref weather, column.SurfaceKelvin < AirPhysics.FreezingKelvin(planet.Condensate));
			}
			accumulator.Add(weather, 1f, PrecipitateOf(planet, column.SurfaceKelvin));

			// What the wind lifts off the ground.
			if (ground.Loose != null && !ground.Water)
			{
				float gust = air.Wind.magnitude * (1f + 0.5f * weather[WeatherChannel.WindGust]);
				float threshold = LiftingWind(ground.Loose, planet, column.SurfaceKelvin);
				float lifted = Mathf.Clamp01((gust - threshold) / Mathf.Max(0.5f, threshold));
				// Damp ground holds together, and rain washes the air clean.
				lifted *= Mathf.Sqrt(Mathf.Clamp01(1f - column.RelativeHumidity)) * (1f - Mathf.Clamp01(weather[WeatherChannel.Precipitation]) * 0.8f);
				if (lifted > 0.001f)
				{
					accumulator.Add(Falling(ground.Loose, lifted), 1f, ground.Loose);
				}
			}

			// What the ground puts out by itself — unless it comes out of vents, when it rises in their
			// plumes and falls out of those, downwind, instead (below).
			if (ground.Emits != null && ground.EmissionRate > 0f && !ground.Vented)
			{
				float amount = Mathf.Clamp01(ground.EmissionRate * Mathf.Max(0f, emissionBoost));
				if (amount > 0.001f)
				{
					accumulator.Add(Falling(ground.Emits, amount), 1f, ground.Emits);
				}
			}

			// What falls out of the plumes overhead and upwind: only with air to hold a plume up.
			if (fallout > 0.001f && VolcanicPlume.CanRise(planet))
			{
				accumulator.Add(FallingFrom(falloutSubstance, Mathf.Clamp01(fallout)), 1f, falloutSubstance);
			}

			substance = accumulator.Substance;
			WeatherFrame frame = accumulator.Resolve();
			// Water is typed here by the scale's own freezing band. Any other condensate was typed
			// above, by its own freezing point, and carried its substance in with it.
			if (planet.Condensate == Condensate.Water)
			{
				frame.RetypeForTemperature(temperatureScale);
			}
			return frame;
		}

		/// <summary>How much of what falls from this column reaches the ground as hail, 0..1.</summary>
		/// <remarks>
		/// <para>
		/// A stone grows only while the rising air holds it up, in the supercooled cloud around 20 K
		/// below freezing. It falls at about √(g·D·ρ_ice/ρ_air) — nine metres a second for a
		/// centimetre stone here — so the largest stone an updraught w keeps aloft is
		/// w²·ρ_air / (g·ρ_ice); the stones that fall out are about half that, having spent their
		/// growth going up and down through it. A couple of centimetres in a strong ordinary storm,
		/// several in a supercell, whose spinning core pulls the air up half again faster than
		/// buoyancy alone. Weaker pull holds bigger stones.
		/// </para>
		/// <para>
		/// Below the freezing level a stone melts, about a millimetre and a half for every kilometre
		/// of warm air on our own world — more where it falls slower and so spends longer in it.
		/// What lands is hail only if it is still a pea, and big stones mean a hail core, where
		/// most of what falls is ice. Shallow convection grows none.
		/// </para>
		/// </remarks>
		public static float HailShare(in WeatherDriver.Synoptic air, in AirColumn column, in PlanetAir planet)
		{
			float landed = HailStoneMetres(air, column, planet);
			return 0.6f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.01f, 0.05f, landed));
		}

		/// <summary>
		/// How big the stones this column's hail lands as, m across: the stone its updraught grows,
		/// less what melts on the way down. Zero where it grows none, or none survive the fall.
		/// See <see cref="HailShare"/>.
		/// </summary>
		public static float HailStoneMetres(in WeatherDriver.Synoptic air, in AirColumn column, in PlanetAir planet)
		{
			if (!column.Deep || planet.Gravity <= 0f)
			{
				return 0f;
			}
			// A rotating updraught gains about half again on its buoyancy from the low pressure in its core.
			float updraft = column.Updraft * (1f + 0.6f * Mathf.Clamp01(air.Mesocyclone));
			float growth = column.HeightOfKelvin(AirPhysics.FreezingKelvin(planet.Condensate) - 20f);
			float growthKelvin = Mathf.Max(20f, column.SurfaceKelvin - column.MeanLapse * growth);
			float airDensity = planet.SurfacePressure * Mathf.Exp(-growth / Mathf.Max(1f, column.ScaleHeight)) / (planet.GasConstant * growthKelvin);
			float grown = 0.5f * updraft * updraft * airDensity / (planet.Gravity * IceDensity);
			float meltPerMetre = 1.5e-6f * Mathf.Sqrt(FishMMO.Shared.Celestial.SurfacePhysics.EarthGravity / planet.Gravity);
			return Mathf.Max(0f, grown - meltPerMetre * column.Freezing);
		}

		/// <summary>How fast a hailstone falls, m/s: its weight against the drag of the air, √(g·D·ρ_ice/ρ_air).</summary>
		/// <remarks>Nine metres a second for a centimetre stone at our own sea level; twenty for a walnut.</remarks>
		public static float HailFallSpeed(float stoneMetres, float gravity, float airDensity)
		{
			return Mathf.Sqrt(Mathf.Max(0f, gravity) * Mathf.Max(0f, stoneMetres) * IceDensity / Mathf.Max(0.01f, airDensity));
		}

		/// <summary>Solid ice, kg/m³.</summary>
		public const float IceDensity = 917f;

		/// <summary>
		/// The wind, at head height, that starts a loose ground moving, m/s.
		/// </summary>
		/// <remarks>
		/// A grain lifts when the drag of the wind at the surface beats its weight and the stickiness
		/// that holds fine grains together (Shao and Lu): heavy grains need more wind, and so do very
		/// fine ones, which cling. Our own sand moves at about six metres a second. Weaker gravity and
		/// thicker air both lower it; that is why the thin air of Mars, for all its dust, needs a gale.
		/// </remarks>
		public static float LiftingWind(WeatherSubstance grain, in PlanetAir planet, float surfaceKelvin)
		{
			float d = Mathf.Max(1e-6f, grain != null ? grain.GrainMetres : 1e-4f);
			float rhoP = grain != null ? grain.GrainDensity : 2500f;
			float rhoA = Mathf.Max(1e-4f, planet.SurfacePressure / (planet.GasConstant * Mathf.Max(20f, surfaceKelvin)));
			float friction = Mathf.Sqrt(0.0123f * (rhoP / rhoA * planet.Gravity * d + 3e-4f / (rhoA * d)));
			// Friction velocity to the wind at head height over open ground: (1/κ)·ln(z/z₀), κ 0.4, z₀ 1 mm.
			return friction * 23f;
		}

		/// <summary>What is falling of a condensate at a temperature: liquid above its freezing point, frozen below.</summary>
		/// <remarks>
		/// Null for water, whose rain, snow and hail are the kinds' own defaults and are typed by the
		/// temperature where they land.
		/// </remarks>
		public static WeatherSubstance PrecipitateOf(in PlanetAir planet, float surfaceKelvin)
		{
			if (planet.Condensate == Condensate.Water)
			{
				return null;
			}
			bool frozen = surfaceKelvin < AirPhysics.FreezingKelvin(planet.Condensate);
			WeatherSubstance best = null;
			// Null until the first substance has loaded.
			System.Collections.Generic.Dictionary<int, WeatherSubstance> loaded = WeatherSubstance.GetCache<WeatherSubstance>();
			if (loaded == null)
			{
				return null;
			}
			foreach (WeatherSubstance s in loaded.Values)
			{
				if (s == null || !s.Condenses || s.Condensate != planet.Condensate)
				{
					continue;
				}
				if (s.Frozen == frozen)
				{
					return s;
				}
				best = best != null ? best : s;
			}
			return best;
		}

		/// <summary>A frame of one substance falling at an amount, in the channel for how it falls.</summary>
		public static WeatherFrame Falling(WeatherSubstance substance, float amount)
		{
			var frame = new WeatherFrame();
			frame[WeatherChannel.Precipitation] = Mathf.Clamp01(amount);
			frame[WeatherChannel.DropSize] = 0.3f;
			frame[ChannelOf(substance)] = 1f;
			return frame;
		}

		/// <summary>A plume's fallout: its substance falling, or plain ash when it has none.</summary>
		public static WeatherFrame FallingFrom(WeatherSubstance substance, float amount)
		{
			if (substance != null)
			{
				return Falling(substance, amount);
			}
			var frame = new WeatherFrame();
			frame[WeatherChannel.Precipitation] = Mathf.Clamp01(amount);
			frame[WeatherChannel.DropSize] = 0.3f;
			frame[WeatherChannel.AshWeight] = 1f;
			return frame;
		}

		/// <summary>
		/// The precipitation channel a substance falls in: sand, ash and snow by what they leave on
		/// the ground, anything liquid (or unknown) as rain.
		/// </summary>
		/// <remarks>
		/// One answer for both ends: <see cref="Falling"/> puts a substance in this channel, and the
		/// presentation dresses only this channel in the substance's colour and speed. A frame
		/// carries one substance, and wearing it on every kind drew rain grey under a volcano's ash
		/// and sand-coloured in a haboob.
		/// </remarks>
		public static WeatherChannel ChannelOf(WeatherSubstance substance)
		{
			switch (substance != null ? substance.Cover : WeatherCoverKind.None)
			{
				case WeatherCoverKind.Sand: return WeatherChannel.SandWeight;
				case WeatherCoverKind.Ash: return WeatherChannel.AshWeight;
				case WeatherCoverKind.Snow: return WeatherChannel.SnowWeight;
				default: return WeatherChannel.RainWeight;
			}
		}

		/// <summary>
		/// Types a condensate's precipitation by whether it is frozen where it lands: all of the
		/// liquid share to snow below its freezing point, all of the frozen share to rain above it.
		/// Hail is left as hail; it is the updraught's and survives the fall or does not
		/// (<see cref="HailStoneMetres"/>).
		/// </summary>
		public static void TypeCondensate(ref WeatherFrame frame, bool frozen)
		{
			float fluid = frame[WeatherChannel.RainWeight] + frame[WeatherChannel.SnowWeight];
			frame[WeatherChannel.RainWeight] = frozen ? 0f : fluid;
			frame[WeatherChannel.SnowWeight] = frozen ? fluid : 0f;
		}
	}
}
