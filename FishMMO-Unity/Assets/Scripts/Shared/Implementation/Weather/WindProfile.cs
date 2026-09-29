using UnityEngine;

namespace FishMMO.Shared.Weather
{
	/// <summary>
	/// How the wind strengthens with height: held back by the ground near it, free above the
	/// boundary layer, and growing through the troposphere to the jet at the tropopause.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Near the ground</b> the wind is slowed by friction and rises as the logarithm of height
	/// (the law of the wall, roughness length 3 cm over open country), from its measured speed at
	/// ten metres to the free wind at the top of the boundary layer — the mixed layer, which reaches
	/// up to about where the day's cumulus form.
	/// </para>
	/// <para>
	/// <b>Above it</b> the wind grows with height because the air is colder toward the poles: in
	/// thermal-wind balance the shear is g/(f·T) times the equator-to-pole temperature gradient,
	/// with f = 2Ω·sin(latitude) the Coriolis parameter. On our own world at 45° that is about three
	/// metres a second per kilometre — a ten metre-a-second breeze under a forty metre-a-second jet
	/// at the tropopause. The gradient is concentrated where the jets run (WeatherDriver.JetBand), and
	/// a world that turns faster has a weaker thermal wind for the same gradient.
	/// </para>
	/// <para>
	/// <b>Above the tropopause</b> the gradient reverses in the stratosphere and the wind falls away.
	/// </para>
	/// <para>
	/// Two uses, kept apart. The drift of the cloud bands must never change with the weather — a band
	/// that sped up would jump across the sky — so their scales come from <see cref="Climatological"/>,
	/// which depends on the world and the latitude only. The lean and stretch of a cloud, which do not
	/// accumulate, may follow the weather of the moment through <see cref="Of"/>.
	/// </para>
	/// </remarks>
	public readonly struct WindProfile
	{
		/// <summary>The roughness length of open country, m.</summary>
		public const float Roughness = 0.03f;
		/// <summary>The height the surface wind is quoted at, m.</summary>
		public const float ReferenceHeight = 10f;

		/// <summary>The wind at ten metres, m/s.</summary>
		public readonly float Surface;
		/// <summary>The top of the boundary layer, m above the ground.</summary>
		public readonly float BoundaryTop;
		/// <summary>The free wind at the top of the boundary layer, m/s.</summary>
		public readonly float Geostrophic;
		/// <summary>The thermal-wind shear above the boundary layer, (m/s) per m.</summary>
		public readonly float Shear;
		/// <summary>The tropopause, m above the ground.</summary>
		public readonly float Tropopause;

		public WindProfile(float surface, float boundaryTop, float geostrophic, float shear, float tropopause)
		{
			Surface = Mathf.Max(0f, surface);
			BoundaryTop = Mathf.Max(ReferenceHeight * 2f, boundaryTop);
			Geostrophic = Mathf.Max(Surface, geostrophic);
			Shear = Mathf.Max(0f, shear);
			Tropopause = Mathf.Max(BoundaryTop + 100f, tropopause);
		}

		/// <summary>The wind at the tropopause, m/s: the jet.</summary>
		public float Jet => Geostrophic + Shear * (Tropopause - BoundaryTop);

		/// <summary>The wind speed at a height above the ground, m/s.</summary>
		public float At(float heightMetres)
		{
			float z = Mathf.Max(0f, heightMetres);
			if (z <= BoundaryTop)
			{
				float surfaceLog = Mathf.Log(ReferenceHeight / Roughness);
				float here = Mathf.Log(Mathf.Max(Roughness * 1.5f, z) / Roughness);
				float top = Mathf.Log(BoundaryTop / Roughness);
				// The law of the wall from the ten-metre wind, blended so it meets the free wind at
				// the top of the layer whatever the two measured speeds are.
				float lawOfTheWall = Surface * here / surfaceLog;
				float atTop = Surface * top / surfaceLog;
				return lawOfTheWall + (Geostrophic - atTop) * Mathf.Clamp01(here / top);
			}
			if (z <= Tropopause)
			{
				return Geostrophic + Shear * (z - BoundaryTop);
			}
			// The stratosphere: the gradient reverses and the wind falls away with height.
			return Jet * Mathf.Exp(-(z - Tropopause) / (0.3f * Tropopause));
		}

		/// <summary>How fast the wind grows with height here, (m/s) per m.</summary>
		public float ShearAt(float heightMetres)
		{
			const float step = 50f;
			return (At(heightMetres + step) - At(Mathf.Max(0f, heightMetres - step))) / (heightMetres > step ? 2f * step : heightMetres + step);
		}

		/// <summary>
		/// The wind with height over a place, from the weather of the moment: for leaning and stretching
		/// cloud, never for drift.
		/// </summary>
		/// <param name="surfaceSpeed">The wind at ten metres, m/s.</param>
		/// <param name="column">The air column: its cloud base tops the mixed layer, its tropopause the troposphere.</param>
		public static WindProfile Of(float surfaceSpeed, in AirColumn column, in PlanetAir planet, float latitudeDegrees, in WindBelts belts)
		{
			float boundary = Mathf.Clamp(column.Base, 300f, 2500f);
			float geostrophic = FreeWind(surfaceSpeed, boundary);
			float shear = ThermalShear(planet, latitudeDegrees, belts, column.SurfaceKelvin);
			return new WindProfile(surfaceSpeed, boundary, geostrophic, shear, column.Tropopause);
		}

		/// <summary>
		/// The wind with height a world has at a latitude on average: for the cloud bands' drift,
		/// which must not change with the weather.
		/// </summary>
		/// <param name="surfaceSpeed">A typical ten-metre wind there, m/s.</param>
		/// <param name="boundaryTop">A typical mixed layer, m.</param>
		/// <param name="tropopause">A typical tropopause, m.</param>
		public static WindProfile Climatological(in PlanetAir planet, float latitudeDegrees, in WindBelts belts, float surfaceSpeed, float boundaryTop, float tropopause)
		{
			float boundary = Mathf.Clamp(boundaryTop, 300f, 2500f);
			return new WindProfile(surfaceSpeed, boundary, FreeWind(surfaceSpeed, boundary),
				ThermalShear(planet, latitudeDegrees, belts, planet.MeanSurfaceKelvin), tropopause);
		}

		/// <summary>The free wind at the top of a boundary layer, m/s: the law of the wall run up from ten metres.</summary>
		public static float FreeWind(float surfaceSpeed, float boundaryTop)
		{
			return Mathf.Max(0f, surfaceSpeed) * Mathf.Log(Mathf.Max(ReferenceHeight * 2f, boundaryTop) / Roughness) / Mathf.Log(ReferenceHeight / Roughness);
		}

		/// <summary>
		/// The thermal-wind shear, (m/s) per m: g/(f·T) times the equator-to-pole temperature gradient,
		/// concentrated where the jets run.
		/// </summary>
		/// <remarks>
		/// The gradient: a sixth or so of the world's mean temperature between its equator and its poles
		/// (about 43 K on our own), over a quarter of a meridian, twice as steep in the jet's baroclinic
		/// zone.
		/// <para>
		/// Inside the tropical cell the balance changes. With the Coriolis force weak, the air cannot
		/// hold a temperature gradient up at all — the overturning flattens it (the weak temperature
		/// gradient balance: what gradient there is scales with f), so g/(fT)·∂T/∂y stops growing
		/// toward the equator and holds at its value on the cell's poleward edge. Evaluating f at
		/// the larger of the latitude and the cell's edge is exactly that. It was floored at fifteen
		/// degrees instead, which put a stronger shear over the tropics than under the jet: at ten
		/// degrees 1.14 times the jet's.
		/// </para>
		/// </remarks>
		public static float ThermalShear(in PlanetAir planet, float latitudeDegrees, in WindBelts belts, float kelvin)
		{
			if (!planet.HasAir || planet.Gravity <= 0f || planet.RotationRate <= 0f)
			{
				return 0f;
			}
			float latitude = Mathf.Max(Mathf.Clamp(belts.CellDegrees, 1f, 90f), Mathf.Abs(latitudeDegrees)) * Mathf.Deg2Rad;
			float coriolis = 2f * planet.RotationRate * Mathf.Sin(latitude);
			float contrast = 0.15f * Mathf.Max(20f, planet.MeanSurfaceKelvin);
			float gradient = contrast / (0.5f * Mathf.PI * Mathf.Max(1000f, planet.RadiusMetres));
			float band = WeatherDriver.JetStrength(latitudeDegrees, belts);
			return planet.Gravity / (coriolis * Mathf.Max(20f, kelvin)) * gradient * 2f * band;
		}
	}
}
