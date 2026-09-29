using UnityEngine;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.Weather
{
	/// <summary>The three regimes of cloud an air makes, lowest first.</summary>
	public enum CloudRegime : byte
	{
		/// <summary>
		/// The column from the condensation level up: a stratus or stratocumulus deck in settled air,
		/// heaps in unsettled air, towers where the air breaks through to the tropopause. One field,
		/// so a tower rises out of a deck rather than sitting in a different layer.
		/// </summary>
		Convective = 0,
		/// <summary>The middle: altostratus under large-scale lifting, altocumulus where it is unstable, between −5 and −25 °C.</summary>
		Layered = 1,
		/// <summary>The top: cirrus, where every drop has frozen and the fall streaks are drawn out by the jet.</summary>
		Ice = 2,
	}

	/// <summary>
	/// One regime of cloud as the renderer draws it: where it sits, how it is shaped, how opaque it
	/// is — every number worked out from the air, none of them authored.
	/// </summary>
	public struct CloudBand
	{
		public CloudRegime Regime;
		/// <summary>Whether the air makes this regime at all here: an ice level above the tropopause has no cirrus.</summary>
		public bool Present;
		/// <summary>The band's shell, m above sea level. For the column, the lowest base and the highest tower anywhere in the air map.</summary>
		public float Bottom, Top;
		/// <summary>Metres one tile of the shape noise covers: how big the cloud masses are.</summary>
		public float NoiseScale;
		/// <summary>Metres one tile of the detail noise covers: the eddies that mix a cloud's edge with the air round it.</summary>
		public float DetailScale;
		/// <summary>
		/// How hard the detail eats in: the critical mixing fraction (<see cref="CloudClimate.CriticalMixingFraction"/>)
		/// — how much of a mixture at the cloud's edge has to be cloud air for any of its water to
		/// survive. Dry surroundings put it near 1 and a cloud's edge is crisp and frayed; saturated
		/// ones put it near 0 and the whole mixing shell stays cloudy and soft.
		/// </summary>
		public float DetailStrength;
		/// <summary>How far the noise is drawn out along the wind: fall streaks under the jet. A whole number, for the drift's wrap.</summary>
		public float Stretch;
		/// <summary>How much the shape turns over with height: 5 on the column makes its cells as tall as they are wide.</summary>
		public float VerticalScale;
		public float BaseSoftness, TopSoftness;
		/// <summary>How much the height a cloud reaches varies from place to place.</summary>
		public float Convection;
		/// <summary>How fast this regime drifts against the column: the wind rises with height.</summary>
		public float WindScale;
		/// <summary>
		/// How opaque: for the column, <c>C</c> in <c>β = C·h^⅔</c> (h above the base); for a layer, its
		/// mean β in 1/m.
		/// </summary>
		public float Extinction;
		/// <summary>
		/// The mean diameter of its drops, µm, which decides how they scatter (<see cref="CloudClimate.MieAsymmetry"/>);
		/// 0 for ice, whose crystals scatter by their own law.
		/// </summary>
		public float DropletDiameter;
		/// <summary>The mean cosine of its scattering: how far forward light keeps going through it.</summary>
		public float Asymmetry;
		/// <summary>The regime's cover at the viewer.</summary>
		public float Coverage;
		/// <summary>The most cover it has anywhere in the air map: a band clear overhead is not clear everywhere.</summary>
		public float MaxCoverage;
		/// <summary>The thinnest this regime's cloud gets anywhere, m: what the march's stride must not step over.</summary>
		public float Thinnest;
		public bool CarriesRain, GrowsStorms;
		public bool Column => Regime == CloudRegime.Convective;

		public string Name
		{
			get
			{
				switch (Regime)
				{
					case CloudRegime.Layered: return "Middle";
					case CloudRegime.Ice: return "High";
					default: return "Low";
				}
			}
		}
	}

	/// <summary>
	/// A world's clouds, from its air: where each regime sits, how it is shaped, how it scatters light.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Two kinds of number, kept strictly apart. <b>Scales</b> are the world's — how big a cloud
	/// cell is, how fast each level drifts, how far cirrus is drawn out — and are worked out once,
	/// from the world's average air, because the renderer reads its noise at positions divided by
	/// them: a scale that moved with the weather would zoom and teleport the whole sky, since those
	/// positions are hundreds of kilometres of accumulated drift. <b>Heights, cover and opacity</b>
	/// are the weather's, and follow the air from moment to moment and place to place.
	/// </para>
	/// <para>
	/// The optics here are the CPU twins of FishCloudVolume.hlsl's, and the tests hold them to real
	/// clouds' numbers (CloudRealismTests). The sources, in one place: Hess, Koepke and Schult 1998
	/// for cloud extinction (stratus 0.04–0.06 /m, cumulus 0.05–0.12 /m, albedo 1); Martin, Johnson and
	/// Spice 1994 for effective radius against volume radius; Jendersie and d'Eon 2023 for the drops'
	/// phase function; the Eddington two-stream solution (Stephens' AT622 notes §15; Bohren 1987) for
	/// light through a thick cloud; Heymsfield 1975 and Heymsfield and Iaquinta 2000 for cirrus
	/// fall streaks and fall speeds.
	/// </para>
	/// </remarks>
	public static class CloudClimate
	{
		public const int BandCount = 3;

		/// <summary>
		/// How much of the water a rising parcel condenses a cumulus keeps, against the air it mixes in
		/// on the way up: about a third (Warner 1955; the figure AirColumn.ExtinctionCoefficient uses).
		/// </summary>
		public const float AdiabaticFraction = 0.35f;

		/// <summary>
		/// How much of it a storm's undiluted core keeps: most of it. Aircraft through cumulonimbus
		/// updraught cores find 0.6–0.9 of adiabatic (Paluch 1979; Heymsfield, Johnson and Dye 1978).
		/// </summary>
		public const float StormAdiabaticFraction = 0.75f;

		/// <summary>
		/// The mean cosine of scattering by ice crystals at visible wavelengths: 0.74–0.78 for the
		/// rough aggregates and bullet rosettes of real cirrus (Baran 2012), far below the 0.87 of drops.
		/// </summary>
		public const float IceAsymmetry = 0.76f;

		/// <summary>
		/// The effective radius of the ice a storm's updraught carries up, µm: 20–40 in anvils and
		/// glaciated tops (Garrett et al. 2005, CRYSTAL-FACE), against about ten for the drops below.
		/// </summary>
		public const float StormIceRadiusMicrons = 30f;

		/// <summary>
		/// A typical updraught in a thunderstorm's core, m/s: ordinary storms 10–20, supercells 30–50
		/// (Markowski and Richardson 2010, ch. 7–8). What a tower above the ordinary heaps' tops must have
		/// had to get there, for how far it leans.
		/// </summary>
		public const float StormUpdraught = 20f;

		/// <summary>
		/// The updraught of a fair-weather cumulus, m/s: 1–5 in the cloud (Stull 1988, ch. 11). The least
		/// any heap has, for how far it leans.
		/// </summary>
		public const float HeapUpdraught = 3f;

		/// <summary>
		/// The effective radius of cirrus ice, µm: cirrus crystals run to tens of microns in effective
		/// radius, and the anvils' figure (<see cref="StormIceRadiusMicrons"/>) is used for both — the
		/// same ice, carried up or formed in place. What a cirrus edge holds, for how it evaporates.
		/// </summary>
		public const float CirrusIceRadiusMicrons = StormIceRadiusMicrons;

		/// <summary>
		/// How wide the shell in which a cloud mixes with the air round it is, as a share of the detail
		/// volume's tile: half its largest eddy, a hundred and seven metres on ours.
		/// </summary>
		/// <remarks>
		/// Aircraft through the edges of trade-wind cumulus find the mixing zone — the evaporatively
		/// cooled, sinking shell — within about a hundred metres of the cloud (Katzwinkel, Siebert, Heus
		/// and Shaw 2014; Heus and Jonker 2008 named it; McMichael et al. 2020 measure ~100 m by Doppler
		/// lidar). HLSL twin: FISH_CLOUD_SHELL_SHARE. It used to be a softness in the noise's own units,
		/// 0.06 + 0.13 × humidity: through the noise's measured gradient that was a ramp eight hundred
		/// metres wide, and 96 % of every cloud sat in it at under half its water — every cloud was
		/// all soft edge and no core, which is what "soft blobs" was.
		/// </remarks>
		public const float MixingShellShare = 0.5f;

		/// <summary>
		/// How far the eddies carry a cloud's edge in and out, in mixing shells: half a shell, the size
		/// of the largest of them (the detail volume's coarsest cells are a quarter of its tile, a
		/// shell half of it). HLSL twin: FISH_CLOUD_EDDY_REACH.
		/// </summary>
		/// <remarks>
		/// Mixing at a cloud's edge is inhomogeneous: the drops in a mixture evaporate in a second or two
		/// (r²/2G|S|, Rogers and Yau 1989 ch. 7, for ten-micron drops in air at 60–90 % humidity), long
		/// before an eddy of tens of metres has stirred it smooth (tens of seconds at the edge's
		/// dissipation rates), so the edge is filaments of cloud and clear air side by side rather than a
		/// uniform thinning (Baker, Corbin and Latham 1980; Beals et al. 2015 find it down to the
		/// centimetre): the eddies move the edge, they do not thin the cloud. Nothing past half a shell
		/// inside the cut — the core — is ever touched.
		/// </remarks>
		public const float EddyReach = 0.5f;

		/// <summary>The sea-level Rayleigh scattering of standard air, red/green/blue, 1/m (the AtmosphereModel's own; Bruneton and Neyret 2008).</summary>
		public static readonly Vector3 RayleighSeaLevel = new Vector3(5.8e-6f, 13.5e-6f, 33.1e-6f);

		/// <summary>What a world's clouds are sized and moved by: fixed for the world.</summary>
		public struct Scales
		{
			/// <summary>The column's shape tile, m: nine times the average cloud base, which puts its cells about one and a half base-heights apart.</summary>
			public float LowTile;
			public float LowDetail;
			public float MidTile;
			public float MidDetail;
			public float HighTile;
			public float HighDetail;
			/// <summary>How far cirrus is drawn out along the wind: a whole number.</summary>
			public float HighStretch;
			/// <summary>How fast the middle drifts against the column: its wind over the wind at the base.</summary>
			public float MidWind;
			/// <summary>How fast the high ice drifts against the column.</summary>
			public float HighWind;
			/// <summary>The average cloud base, m.</summary>
			public float MeanBase;
			public float MeanTropopause;
			/// <summary>The world's steady wind at the cloud base, m/s: what the column and the weather drift at.</summary>
			public float SteeringWind;
			/// <summary>How fast the world's wind grows with height at the ice level, (m/s) per m.</summary>
			public float Shear;
			/// <summary>How fast cirrus ice falls on this world, m/s.</summary>
			public float IceFallSpeed;
			/// <summary>How far cirrus fall streaks trail, m.</summary>
			public float StreakLength;
		}

		/// <summary>A world's scales, from its average air at a latitude.</summary>
		/// <remarks>
		/// <list type="bullet">
		/// <item>Convective cells are spaced about one and a half times the depth of the mixed layer
		/// under them, which is the cloud base: the shape volume's cells are a sixth of its tile, so
		/// the tile is nine base-heights. The wisps are a fifth of the base.</item>
		/// <item>Each level drifts at the world's own wind at its height, against the wind at the base
		/// (<see cref="WindProfile.Climatological"/>): the free wind at the base is the steady drift
		/// the column and the weather field are carried by, and above it the thermal wind grows to the
		/// jet. It was one plus the height over half the depth of the weather — 1.65 and 2.29 on our
		/// world, where the thermal wind makes it about 2 and 3.</item>
		/// <item>Cirrus is drawn out by its own fall streaks: ice falling at its terminal speed through
		/// the shear trails behind the head it fell from (<see cref="FallStreakLength"/>). It was one
		/// plus the jet over 3.5 m/s, which was a number, not a reason.</item>
		/// </list>
		/// </remarks>
		public static Scales ScalesFor(in PlanetAir planet, float latitudeDegrees, uint worldSeed)
		{
			AirColumn mean = AirColumn.Of(planet, planet.MeanSurfaceKelvin, 0.5f, 0f, 0.25f);
			float baseMetres = Mathf.Max(200f, mean.Base);
			float trop = Mathf.Max(baseMetres + 1000f, mean.Tropopause);
			float midHeight = mean.HeightOfKelvin(AirPhysics.FreezingKelvin(planet.Condensate) - 15f);
			float iceBottom = mean.IceLevel;
			float iceTop = Mathf.Min(mean.Tropopause, iceBottom + IceBandDepth(mean));
			var scales = new Scales
			{
				MeanBase = baseMetres,
				MeanTropopause = trop,
				LowTile = Mathf.Clamp(9f * baseMetres, 6000f, 20000f),
				LowDetail = Mathf.Clamp(0.2f * baseMetres, 150f, 800f),
			};
			scales.MidTile = scales.LowTile * 0.75f;
			scales.MidDetail = scales.LowDetail * 1.85f;
			scales.HighTile = scales.LowTile * 1.17f;
			scales.HighDetail = scales.LowDetail * 3.2f;

			WindBelts belts = WindBelts.For(planet);
			scales.SteeringWind = WeatherDriver.AdvectionSpeed(worldSeed, latitudeDegrees, belts);
			WindProfile wind = ClimatologicalWind(planet, latitudeDegrees, belts, scales.SteeringWind, baseMetres, trop);
			float atBase = Mathf.Max(0.1f, wind.At(baseMetres));
			scales.MidWind = Mathf.Max(1f, wind.At(midHeight) / atBase);
			float iceMiddle = 0.5f * (iceBottom + Mathf.Max(iceBottom, iceTop));
			scales.HighWind = Mathf.Max(scales.MidWind, wind.At(iceMiddle) / atBase);

			scales.Shear = wind.ShearAt(iceMiddle);
			scales.IceFallSpeed = IceFallSpeed(planet);
			scales.StreakLength = FallStreakLength(scales.Shear, FallStreakDepth(planet), scales.IceFallSpeed);
			scales.HighStretch = Mathf.Round(Mathf.Clamp(1f + scales.StreakLength / (scales.HighTile / 6f), 1f, 12f));
			return scales;
		}

		/// <summary>
		/// The world's wind with height at a latitude, pinned so that the free wind at the cloud base
		/// is the steady drift the weather is carried by.
		/// </summary>
		/// <remarks>
		/// <see cref="WindProfile.Climatological"/> asks for a ten-metre wind; the world's own steady
		/// wind is the drift's, and it is the wind at the top of the mixed layer, where the column's
		/// base is. So the ten-metre wind is that brought down the law of the wall.
		/// </remarks>
		public static WindProfile ClimatologicalWind(in PlanetAir planet, float latitudeDegrees, in WindBelts belts, float steeringWind, float baseMetres, float tropopause)
		{
			float boundary = Mathf.Clamp(baseMetres, 300f, 2500f);
			float surface = Mathf.Max(0f, steeringWind) * Mathf.Log(WindProfile.ReferenceHeight / WindProfile.Roughness)
				/ Mathf.Log(Mathf.Max(WindProfile.ReferenceHeight * 2f, boundary) / WindProfile.Roughness);
			return WindProfile.Climatological(planet, latitudeDegrees, belts, surface, boundary, tropopause);
		}

		/// <summary>How deep the ice regime is above the ice level, m.</summary>
		public static float IceBandDepth(in AirColumn column) => Mathf.Max(600f, 0.08f * column.ScaleHeight);

		/// <summary>
		/// How much low cloud an air asks for, 0..1: the systems' and fronts' share, before the
		/// formations. Air too dry to condense before the lid stops it rising has none.
		/// </summary>
		public static float LowCover(in WeatherDriver.Synoptic air, in AirColumn column)
		{
			return WeatherDriver.SynopticCloudCover(air) * column.LowCloudAllowed;
		}

		/// <summary>
		/// How much middle cloud: altostratus arrives only once large-scale lifting has closed the low
		/// sky over — a front's sheet — and altocumulus where the middle air is unstable.
		/// </summary>
		public static float MidCover(in WeatherDriver.Synoptic air, in AirColumn column)
		{
			float sheet = Mathf.InverseLerp(0.55f, 1f, WeatherDriver.SynopticCloudCover(air));
			float cells = Mathf.Clamp01((air.Humidity - 0.45f) / 0.3f) * column.Vigour * 0.35f;
			return Mathf.Clamp01(Mathf.Max(sheet, cells));
		}

		/// <summary>
		/// How much cirrus: the ice the upper air holds, which is damp ahead of a low — the first sign
		/// of a front, a day out — and dry under a settled high.
		/// </summary>
		public static float HighCover(in WeatherDriver.Synoptic air, in AirColumn column)
		{
			if (column.IceLevel >= column.Tropopause - 150f)
			{
				return 0f;
			}
			return Mathf.Clamp01(0.6f * (air.Humidity - 0.3f) + 0.2f * Mathf.Max(0f, -air.Pressure));
		}

		/// <summary>The extremes of the air map a column band needs: where its cloud can be at all.</summary>
		public struct MapExtremes
		{
			public float LowestBase, HighestTower, ThinnestDeck;
			public float MaxLow, MaxMid, MaxHigh;
			public bool Valid;
		}

		/// <summary>
		/// The three regimes for the viewer's air, their shells widened to cover the whole air map.
		/// </summary>
		public static void Bands(in Scales scales, in PlanetAir planet, in WeatherDriver.Synoptic air, in AirColumn viewer, in MapExtremes map, CloudBand[] into)
		{
			if (into == null || into.Length < BandCount)
			{
				return;
			}
			float rh = viewer.RelativeHumidity;
			float liquidDensity = AirPhysics.LiquidDensity(planet.Condensate);

			// The column: from the lowest base anywhere to the highest a tower can reach anywhere.
			float lowest = map.Valid ? Mathf.Min(map.LowestBase, viewer.Base) : viewer.Base;
			float highest = map.Valid ? Mathf.Max(map.HighestTower, viewer.TowerCeiling) : viewer.TowerCeiling;
			float lowCover = CloudClimate.LowCover(air, viewer);
			// Its drops at the middle of an ordinary cloud here, where most of what is seen of it is.
			float columnDrops = DropletDiameterMicrons(ColumnWater(viewer, 0.5f * Mathf.Max(200f, viewer.Top - viewer.Base)),
				planet.DropletsPerCubicMetre, planet.Water, liquidDensity);
			into[0] = new CloudBand
			{
				Regime = CloudRegime.Convective,
				Present = true,
				Bottom = lowest,
				Top = Mathf.Max(lowest + 300f, highest),
				NoiseScale = scales.LowTile,
				DetailScale = scales.LowDetail,
				// Dry air around a cloud evaporates its edge: crisp and frayed. Damp air leaves it soft.
				// It was 0.3 + 0.4 × dryness, a strength with no reason behind it, applied to a detail
				// noise whose values barely varied: it thinned every edge alike and frayed none.
				DetailStrength = ColumnEdgeCritical(viewer, planet),
				Stretch = 1f,
				VerticalScale = 5f,
				BaseSoftness = 0.06f,
				TopSoftness = 0.65f,
				Convection = 0.8f,
				WindScale = 1f,
				Extinction = viewer.ExtinctionCoefficient(planet),
				DropletDiameter = columnDrops,
				Asymmetry = MieAsymmetry(columnDrops),
				Coverage = lowCover,
				MaxCoverage = map.Valid ? Mathf.Max(map.MaxLow, lowCover) : lowCover,
				Thinnest = Mathf.Max(80f, map.Valid ? Mathf.Min(map.ThinnestDeck, viewer.Top - viewer.Base) : viewer.Top - viewer.Base),
				CarriesRain = true,
				GrowsStorms = true,
			};

			// The middle: from −5 to −25 degrees of its condensate's freezing point.
			float freeze = AirPhysics.FreezingKelvin(planet.Condensate);
			float midBottom = Mathf.Max(viewer.Base + 500f, viewer.HeightOfKelvin(freeze - 5f));
			float midTop = Mathf.Min(viewer.Tropopause - 100f, Mathf.Max(midBottom + 400f, viewer.HeightOfKelvin(freeze - 25f)));
			float midCover = MidCover(air, viewer);
			float midExtinction = viewer.LayerExtinction(planet, midBottom, midTop);
			// Still liquid, supercooled: its drops from what they take out, which is 2πNr².
			float midDrops = DiameterFromExtinction(midExtinction, planet.DropletsPerCubicMetre, planet.Water);
			into[1] = new CloudBand
			{
				Regime = CloudRegime.Layered,
				Present = midTop - midBottom > 200f,
				Bottom = midBottom,
				Top = midTop,
				NoiseScale = scales.MidTile,
				DetailScale = scales.MidDetail,
				DetailStrength = LayerEdgeCritical(viewer, planet, midBottom, midTop, midExtinction, 0.5e-6f * midDrops, false),
				Stretch = 1f,
				VerticalScale = 2f,
				BaseSoftness = 0.3f,
				TopSoftness = 0.4f,
				// A sheet in settled air, cells where the middle air is unstable.
				Convection = Mathf.Lerp(0.1f, 0.6f, viewer.Vigour),
				WindScale = scales.MidWind,
				Extinction = midExtinction,
				DropletDiameter = midDrops,
				Asymmetry = MieAsymmetry(midDrops),
				Coverage = midCover,
				MaxCoverage = map.Valid ? Mathf.Max(map.MaxMid, midCover) : midCover,
				Thinnest = midTop - midBottom,
			};

			// The top: from where every drop freezes, up toward the tropopause.
			float iceBottom = viewer.IceLevel;
			float iceTop = Mathf.Min(viewer.Tropopause, iceBottom + IceBandDepth(viewer));
			float highCover = HighCover(air, viewer);
			float highExtinction = viewer.LayerExtinction(planet, iceBottom, iceTop);
			into[2] = new CloudBand
			{
				Regime = CloudRegime.Ice,
				Present = iceTop - iceBottom > 150f,
				Bottom = iceBottom,
				Top = iceTop,
				NoiseScale = scales.HighTile,
				DetailScale = scales.HighDetail,
				DetailStrength = LayerEdgeCritical(viewer, planet, iceBottom, iceTop, highExtinction, CirrusIceRadiusMicrons * 1e-6f, true),
				Stretch = scales.HighStretch,
				VerticalScale = 2f,
				BaseSoftness = 0.35f,
				TopSoftness = 0.45f,
				Convection = 0f,
				WindScale = scales.HighWind,
				Extinction = highExtinction,
				DropletDiameter = 0f,
				Asymmetry = IceAsymmetry,
				Coverage = highCover,
				MaxCoverage = map.Valid ? Mathf.Max(map.MaxHigh, highCover) : highCover,
				Thinnest = iceTop - iceBottom,
			};
		}

		// ── The drops ─────────────────────────────────────────────────────

		/// <summary>
		/// The water a column's cloud holds at a height above its base, kg/m³: what a rising parcel
		/// condenses, less what the air it mixes in on the way up takes back (<see cref="AdiabaticFraction"/>).
		/// It grows in a straight line from nothing at the base — which is why a base is flat and thin
		/// and a crown dense.
		/// </summary>
		public static float ColumnWater(in AirColumn column, float aboveBase)
		{
			return Mathf.Max(0f, column.CondensedPerMetre) * AdiabaticFraction * Mathf.Max(0f, aboveBase);
		}

		/// <summary>
		/// The mean diameter of a cloud's drops, µm, from how much water it holds and how many drops
		/// that is shared between.
		/// </summary>
		/// <remarks>
		/// The drops' volume radius is what the water divides into; what scatters light is the
		/// effective radius, the ratio of their volume to their cross-section, which is larger by the
		/// spread of sizes: r_e³ = r_v³/k, with k 0.80 in clean maritime air and 0.67 in continental
		/// (Martin, Johnson and Spice 1994). A world's land and sea share decides which.
		/// </remarks>
		public static float DropletDiameterMicrons(float liquidWater, float dropsPerCubicMetre, float water01, float liquidDensity = 1000f)
		{
			float n = Mathf.Max(1e6f, dropsPerCubicMetre);
			float volumeRadius = Mathf.Pow(3f * Mathf.Max(0f, liquidWater) / (4f * Mathf.PI * Mathf.Max(1f, liquidDensity) * n), 1f / 3f);
			return 2e6f * volumeRadius / Mathf.Pow(SpreadFactor(water01), 1f / 3f);
		}

		/// <summary>The same, from what the drops take out of the light: 2πNr² for their volume radius.</summary>
		public static float DiameterFromExtinction(float extinction, float dropsPerCubicMetre, float water01)
		{
			float n = Mathf.Max(1e6f, dropsPerCubicMetre);
			float volumeRadius = Mathf.Sqrt(Mathf.Max(0f, extinction) / (2f * Mathf.PI * n));
			return 2e6f * volumeRadius / Mathf.Pow(SpreadFactor(water01), 1f / 3f);
		}

		/// <summary>Martin et al.'s k: 0.67 over land, 0.80 over the open sea.</summary>
		private static float SpreadFactor(float water01) => Mathf.Lerp(0.67f, 0.80f, Mathf.Clamp01(water01));

		/// <summary>A cloud's extinction, 1/m, from its water and its drops' effective radius: 3·LWC/(2·ρ·r_e).</summary>
		public static float Extinction(float liquidWater, float effectiveRadiusMetres, float liquidDensity = 1000f)
		{
			return 3f * Mathf.Max(0f, liquidWater) / (2f * Mathf.Max(1f, liquidDensity) * Mathf.Max(1e-7f, effectiveRadiusMetres));
		}

		/// <summary>
		/// The Jendersie–d'Eon fit of Mie scattering by water drops of a mean diameter (µm, 5–50): a
		/// Henyey–Greenstein lobe for the diffraction spike and a Draine lobe for the rest.
		/// </summary>
		/// <remarks>
		/// Jendersie and d'Eon, "An Approximate Mie Scattering Function for Fog and Cloud Rendering",
		/// SIGGRAPH 2023 Talks, equations 4–7. The HG lobe is a spike within a degree of the light
		/// (g 0.98–0.997); the Draine lobe carries the broad forward glow, the flat sides and the rise
		/// toward the back that a single HG cannot. HLSL twin: FishCloudMieParams.
		/// </remarks>
		public static void MieParameters(float diameterMicrons, out float spikeG, out float bodyG, out float alpha, out float bodyWeight)
		{
			float d = Mathf.Clamp(diameterMicrons, 5f, 50f);
			spikeG = Mathf.Exp(-0.0990567f / (d - 1.67154f));
			bodyG = Mathf.Exp(-2.20679f / (d + 3.91029f) - 0.428934f);
			alpha = Mathf.Exp(3.62489f - 8.29288f / (d + 5.52825f));
			bodyWeight = Mathf.Exp(-0.599085f / (d - 0.641583f) - 0.665888f);
		}

		/// <summary>The drops' phase function at a scattering angle's cosine, per steradian (twin of FishCloudPhaseMie).</summary>
		public static float MiePhase(float cosAngle, float diameterMicrons)
		{
			MieParameters(diameterMicrons, out float spikeG, out float bodyG, out float alpha, out float weight);
			float draine = HenyeyGreenstein(cosAngle, bodyG) * (1f + alpha * cosAngle * cosAngle) / (1f + alpha * (1f + 2f * bodyG * bodyG) / 3f);
			return (1f - weight) * HenyeyGreenstein(cosAngle, spikeG) + weight * draine;
		}

		/// <summary>
		/// The mean cosine of the drops' scattering: how much of its direction light keeps at each
		/// bounce, 0.86–0.89 for cloud drops. This is the g in every "(1 − g)τ" of diffusion.
		/// </summary>
		/// <remarks>
		/// Draine's lobe's mean cosine is g(1 + α(3 + 2g²)/5)/(1 + α(1 + 2g²)/3) (Draine 2003); the
		/// spike's is its own g.
		/// </remarks>
		public static float MieAsymmetry(float diameterMicrons)
		{
			if (diameterMicrons <= 0f)
			{
				return IceAsymmetry;
			}
			MieParameters(diameterMicrons, out float spikeG, out float bodyG, out float alpha, out float weight);
			float g2 = bodyG * bodyG;
			float body = bodyG * (1f + alpha * (3f + 2f * g2) / 5f) / (1f + alpha * (1f + 2f * g2) / 3f);
			return (1f - weight) * spikeG + weight * body;
		}

		public static float HenyeyGreenstein(float cosAngle, float g)
		{
			float g2 = g * g;
			return (1f - g2) / (4f * Mathf.PI * Mathf.Pow(Mathf.Max(1e-6f, 1f + g2 - 2f * g * cosAngle), 1.5f));
		}

		// ── Light through cloud ───────────────────────────────────────────

		/// <summary>
		/// How much of the light falling diffusely on a cloud of this optical depth gets through, by
		/// scattering its way there: 1/(1 + ¾(1 − g)τ).
		/// </summary>
		/// <remarks>
		/// A cloud absorbs almost nothing, so what does not come out of the top comes out of the bottom.
		/// Beer's law, e^−τ, is only the light that has not been scattered at all: through a cumulus of
		/// optical depth sixty it is 10⁻²⁶, which is black. The light that has been scattered wanders
		/// through by diffusion, and in the Eddington approximation its share falls only as one over the
		/// optical depth, scaled by how little of its direction each bounce loses (1 − g): about 15 %
		/// through that cumulus, 3 % through a towering congestus, 1 % under a cumulonimbus. That is
		/// why a thick cloud's underside is dark grey and not black. (The hemispheric two-stream
		/// closure gives ½ in place of ¾ — Stephens, CSU AT622 §15, eq. 15.20; the two bracket it.)
		/// </remarks>
		public static float DiffuseTransmission(float opticalDepth, float asymmetry)
		{
			return 1f / (1f + 0.75f * (1f - Mathf.Clamp(asymmetry, 0f, 0.99f)) * Mathf.Max(0f, opticalDepth));
		}

		/// <summary>
		/// How much of the sun reaches the ground under a cloud, against the open sky: the light that
		/// came straight through and the light that was scattered through, for an optical depth
		/// measured along the sun's own ray.
		/// </summary>
		/// <remarks>
		/// The Eddington solution for a collimated beam on a conservative layer (Joseph, Wiscombe and
		/// Weinman 1976; Meador and Weaver 1980): T(μ₀) = [(⅔ + μ₀) + (⅔ − μ₀)e^(−τ/μ₀)] / (4/3 + (1 − g)τ),
		/// with τ the layer's vertical depth. A cloud shadow is not black: it is the sky's light plus
		/// this. HLSL twin: FishCloudGroundTransmission, for the cloud shadow cookie.
		/// </remarks>
		public static float GroundTransmission(float slantOpticalDepth, float sunCosine, float asymmetry)
		{
			float mu = Mathf.Clamp(sunCosine, 0.05f, 1f);
			float slant = Mathf.Max(0f, slantOpticalDepth);
			float direct = Mathf.Exp(-slant);
			float vertical = slant * mu;
			float total = ((2f / 3f + mu) + (2f / 3f - mu) * direct) / (4f / 3f + (1f - Mathf.Clamp(asymmetry, 0f, 0.99f)) * vertical);
			return Mathf.Clamp01(Mathf.Max(direct, total));
		}

		/// <summary>
		/// The optical depth up through a convective column's cloud from its base to a height above it:
		/// the integral of β = C·h^⅔, held at a ceiling where the drops can hold no more.
		/// </summary>
		/// <param name="c">The column's C (<see cref="AirColumn.ExtinctionCoefficient"/>).</param>
		/// <param name="aboveBase">How far up, m.</param>
		/// <param name="ceiling">The most extinction the drops reach, 1/m.</param>
		public static float ColumnOpticalDepth(float c, float aboveBase, float ceiling = 0.15f)
		{
			float h = Mathf.Max(0f, aboveBase);
			if (c <= 0f)
			{
				return 0f;
			}
			float capHeight = Mathf.Pow(Mathf.Max(0f, ceiling) / c, 1.5f);
			if (h <= capHeight)
			{
				return 0.6f * c * Mathf.Pow(h, 5f / 3f);
			}
			return 0.6f * c * Mathf.Pow(capHeight, 5f / 3f) + ceiling * (h - capHeight);
		}

		/// <summary>
		/// How much a storm's glaciated cloud takes out against the liquid cloud of the same water:
		/// ρ_liquid·r_drop / (ρ_ice·r_ice).
		/// </summary>
		/// <remarks>
		/// Extinction is 3·W/(2ρ·r_e) for any particles, so the same water frozen into crystals three times
		/// the drops' size takes out a third as much. Above its freezing level a cumulonimbus is ice
		/// (Rosenfeld and Woodley 2000 find the last liquid at −38 °C), and its upper half and its
		/// anvil are far thinner than the solid liquid base under them.
		/// </remarks>
		public static float GlaciatedRatio(float dropletDiameterMicrons, in PlanetAir planet)
		{
			float liquid = AirPhysics.LiquidDensity(planet.Condensate);
			// Ice is lighter than its liquid for water; for the other condensates the solid and the
			// liquid are near enough the same density.
			float solid = planet.Condensate == Condensate.Water ? 917f : liquid;
			float drop = Mathf.Max(2.5f, 0.5f * dropletDiameterMicrons);
			return Mathf.Clamp01(liquid * drop / (solid * StormIceRadiusMicrons));
		}

		// ── Wind in the clouds ───────────────────────────────────────────

		/// <summary>
		/// How fast cirrus ice falls on a world, m/s: half a metre a second on ours.
		/// </summary>
		/// <remarks>
		/// Cirrus crystals are a tenth of a millimetre or so and fall at 0.2–0.8 m/s (Heymsfield and
		/// Iaquinta 2000). At that size they are in Stokes' regime, v = g·ρ_ice·D²/(18μ), where the drag
		/// is the air's viscosity and not its density: so the speed goes with the pull and not with how
		/// much air there is.
		/// </remarks>
		public static float IceFallSpeed(in PlanetAir planet)
		{
			return 0.5f * Mathf.Max(0.05f, planet.Gravity) / SurfacePhysics.EarthGravity;
		}

		/// <summary>
		/// How far below its head a cirrus fall streak trails, m: a kilometre and a half on ours
		/// (Heymsfield 1975: uncinus trails 1–2 km), in proportion to the world's scale height, which
		/// is how deep the drier air the ice sublimes into lies.
		/// </summary>
		public static float FallStreakDepth(in PlanetAir planet)
		{
			return 1500f * Mathf.Clamp(planet.ScaleHeight / 8400f, 0.2f, 5f);
		}

		/// <summary>
		/// How far a fall streak trails behind its head, m: ice falling at v through a wind that slows by
		/// S every metre down is left behind by S·D²/(2v) over a fall of D.
		/// </summary>
		public static float FallStreakLength(float shear, float fallDepth, float fallSpeed)
		{
			return Mathf.Max(0f, shear) * fallDepth * fallDepth / (2f * Mathf.Max(0.02f, fallSpeed));
		}

		/// <summary>
		/// How far downwind of its foot the air in a cloud at a height has been carried, m: a parcel
		/// rising at w through a wind that grows with height drifts ∫(U(z) − U(base))dz / w on the way.
		/// </summary>
		/// <remarks>
		/// This is why towers lean. A heap rising at a few metres a second through the thermal wind leans a
		/// few tens of degrees; a storm's updraught, ten times faster, stands nearly upright near the
		/// ground and still carries its top kilometres downwind by the tropopause. Integrated through
		/// the whole profile, boundary layer and all: the shader's closed form (<see cref="ShearedLean"/>)
		/// is held to this.
		/// </remarks>
		public static float Lean(in WindProfile wind, float baseMetres, float heightMetres, float updraught)
		{
			float rise = heightMetres - baseMetres;
			if (rise <= 0f)
			{
				return 0f;
			}
			const int Steps = 64;
			float dz = rise / Steps;
			float atBase = wind.At(baseMetres);
			float sum = 0f;
			for (int i = 0; i < Steps; i++)
			{
				float z = baseMetres + (i + 0.5f) * dz;
				sum += (wind.At(z) - atBase) * dz;
			}
			return sum / Mathf.Max(0.1f, updraught);
		}

		/// <summary>
		/// The shader's closed form of the lean (FishCloudLean): the thermal wind's straight-line shear
		/// S above the base, heaps rising at their own updraught to the ordinary cloud top, towers
		/// above it at a storm's.
		/// </summary>
		/// <remarks>
		/// With U(z) − U(base) = S(z − base), a parcel at w leans S(z − base)²/(2w). Anything above the
		/// ordinary heaps' tops is a tower, which only got there on a storm's updraught: the part of
		/// its climb above that height is taken at that speed, so the lean is continuous and never
		/// turns back. It stops growing at the tropopause, where the shear reverses.
		/// </remarks>
		public static float ShearedLean(float shear, float baseMetres, float ordinaryTop, float tropopause, float heapUpdraught, float towerUpdraught, float height)
		{
			float top = Mathf.Max(baseMetres, Mathf.Min(ordinaryTop, tropopause));
			float z = Mathf.Min(height, tropopause);
			float low = Mathf.Clamp(z - baseMetres, 0f, top - baseMetres);
			float high = Mathf.Max(0f, z - top);
			float heap = Mathf.Max(0.5f, heapUpdraught);
			float tower = Mathf.Max(heap, towerUpdraught);
			// ∫ S(z−b)/w dz, piecewise in w: the heap part, then the tower part from the top upward.
			return Mathf.Max(0f, shear) * (low * low / (2f * heap) + (high * high * 0.5f + high * (top - baseMetres)) / tower);
		}

		// ── The light around the clouds ──────────────────────────────────

		/// <summary>
		/// A clear sky's light against the sun's: what a white surface facing up shows under the sky alone,
		/// over what one facing the sun shows under the sun alone (diffuse horizontal over direct normal).
		/// </summary>
		/// <remarks>
		/// The air lets through t^m of the beam over m air masses (t = 0.8 over our own air, the whole
		/// visible band with its haze and ozone) and scatters the rest, seven tenths of it forward and
		/// down — so the ground gets sin(h)·(1 − t^m)·0.7 of the sky for t^m of the sun. Ours comes out at
		/// 0.18 overhead, 0.20 at 30°, 0.30 at 10°, 0.55 at 5° and 1.8 at 2°, as clear-sky measurements
		/// run. A world with more air scatters more of its sun into its sky.
		/// </remarks>
		public static float ClearSkyDiffuseRatio(float sunAltitudeDegrees, float airRelative)
		{
			float altitude = Mathf.Clamp(sunAltitudeDegrees, 0.5f, 90f);
			float airMass = AtmosphereModel.AirMass(altitude);
			float perMass = Mathf.Exp(-0.223f * Mathf.Max(0.02f, airRelative));
			float direct = Mathf.Max(1e-3f, Mathf.Pow(perMass, airMass));
			return Mathf.Sin(altitude * Mathf.Deg2Rad) * (1f - direct) * 0.7f / direct;
		}

		/// <summary>
		/// How much of the scene's ambient sky light is the sky's real light on a cloud, 0..1: the clear
		/// sky's share of the light (<see cref="ClearSkyDiffuseRatio"/>) against the share the ambient
		/// claims. The ambient the world is lit with is set for a scene with no bounce light — half the sun
		/// at noon, so a wall in shade is not black — where the real sky is under a fifth of it; so a
		/// cloud takes about a third of it by day. As the light sets the sky becomes most of the light
		/// there is, and the share goes to all of it.
		/// </summary>
		/// <param name="lightAltitudeDegrees">How high the light is.</param>
		/// <param name="lightLuminance">Its luminance on a surface facing it, in the world's light units.</param>
		/// <param name="ambientLuminance">The ambient sky's, in the same units.</param>
		public static float SkyShareOfAmbient(float lightAltitudeDegrees, float lightLuminance, float ambientLuminance, float airRelative)
		{
			if (ambientLuminance <= 1e-5f)
			{
				return 1f;
			}
			float physical = ClearSkyDiffuseRatio(lightAltitudeDegrees, airRelative) * Mathf.Max(0f, lightLuminance);
			float share = Mathf.Clamp01(physical / ambientLuminance);
			// Below ten degrees the sky takes over the light, and past the horizon it is all of it.
			return Mathf.Lerp(1f, share, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 10f, lightAltitudeDegrees)));
		}

		/// <summary>
		/// How much of the light falling on the ground it sends back up, averaged over a world: the
		/// open sea 0.06 (Payne 1972), land about 0.2 (vegetation 0.15–0.2, desert 0.3–0.4), a frozen world's
		/// snow and ice 0.6.
		/// </summary>
		/// <remarks>
		/// What lights a cloud's base from below. Over snow a cumulus base is nearly as bright as its
		/// sides; over the sea it is dark.
		/// </remarks>
		public static float GroundAlbedo(in PlanetAir planet)
		{
			float open = Mathf.Lerp(0.2f, 0.06f, Mathf.Clamp01(planet.Water));
			float freeze = AirPhysics.FreezingKelvin(planet.Condensate);
			float frozen = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(freeze - 5f, freeze - 15f, planet.MeanSurfaceKelvin));
			return Mathf.Lerp(open, 0.6f, frozen);
		}

		/// <summary>
		/// The extinction of the dust and haze in a world's air at the ground, 1/m: what
		/// <see cref="AirPhysics.HazeDistance"/> counts besides the air itself.
		/// </summary>
		public static float AerosolExtinction(float airRelative, float relativeHumidity)
		{
			float total = 2f / Mathf.Max(1f, AirPhysics.HazeDistance(airRelative, relativeHumidity));
			return Mathf.Max(0f, total - Mathf.Max(0.02f, airRelative) * 1.16e-5f);
		}

		/// <summary>How fast the haze thins with height, m: 1.2 km on ours (Bruneton and Neyret 2008), with the world's own air.</summary>
		public static float AerosolScaleHeight(in PlanetAir planet) => 1200f * Mathf.Clamp(planet.ScaleHeight / 8400f, 0.2f, 5f);

		/// <summary>
		/// How much more of the sun a height sees than the ground does, red/green/blue: the air and the
		/// haze between, as the sky model draws a sun.
		/// </summary>
		/// <remarks>
		/// At noon it is a few per cent. At sunset the sun's light crosses eight or nine thicknesses of
		/// air, most of it near the ground, and a cloud eight kilometres up has two thirds of that
		/// beneath it: its sun is whiter and brighter than the one on the ground — about 1.6, 2 and 2.6
		/// times in red, green and blue on ours — the gold and white cirrus that stay lit after the
		/// ground has gone orange and grey. Worked out as the sky model works out its own sun
		/// (AtmosphereModel: what gets through, as a colour whitened by three tenths, its strength
		/// softened to the 0.45 power, the air mass held at the disc's 8.5 thicknesses) at the height and
		/// at the ground, and handed over as the ratio, so it applies to whatever star and whatever
		/// tint the sky has already given its sun.
		/// </remarks>
		public static Vector3 SunGainAloft(in PlanetAir planet, float aerosolExtinction, float sunAltitudeDegrees, float heightMetres)
		{
			Vector3 ground = SunAt(planet, aerosolExtinction, sunAltitudeDegrees, 0f);
			Vector3 aloft = SunAt(planet, aerosolExtinction, sunAltitudeDegrees, Mathf.Max(0f, heightMetres));
			return new Vector3(
				Mathf.Clamp(aloft.x / Mathf.Max(1e-5f, ground.x), 0.25f, 8f),
				Mathf.Clamp(aloft.y / Mathf.Max(1e-5f, ground.y), 0.25f, 8f),
				Mathf.Clamp(aloft.z / Mathf.Max(1e-5f, ground.z), 0.25f, 8f));
		}

		/// <summary>
		/// How much more of whatever lights the clouds a band sees than the ground does: the sun's gain
		/// aloft (<see cref="SunGainAloft"/>) when the light the ground is given has crossed the air, and
		/// none when it has not.
		/// </summary>
		/// <remarks>
		/// The gain is a ratio, what gets through down to the band over what gets through down to the
		/// ground, and it only means anything applied to a light the ground was given AFTER the air: the
		/// sun, whose colour and strength are what the sky model lets through at its altitude, reddened
		/// and dimmed by the air mass. A moon is not drawn through the air — its light is the profile's
		/// moonlight, tinted by the star, scaled by its phase, its size and how high it stands, and the
		/// ground's moon lamp is given exactly that — so there is no reddening at the ground for the
		/// ratio to take back out, and applied to it the ratio made the clouds' moon up to 1.8 times the
		/// ground's (bluer and brighter by an air it never crossed). The band sees the moon the ground
		/// sees.
		/// </remarks>
		public static Vector3 LightGainAloft(in PlanetAir planet, float aerosolExtinction, float lightAltitudeDegrees, float heightMetres, bool groundLightCrossedAir)
		{
			return groundLightCrossedAir ? SunGainAloft(planet, aerosolExtinction, lightAltitudeDegrees, heightMetres) : Vector3.one;
		}

		/// <summary>The sun the sky model would draw from a height: the colour of what the air above lets through, and its softened strength.</summary>
		private static Vector3 SunAt(in PlanetAir planet, float aerosolExtinction, float sunAltitudeDegrees, float heightMetres)
		{
			float airMass = Mathf.Min(8.5f, AtmosphereModel.AirMass(sunAltitudeDegrees));
			float rayleighHeight = Mathf.Max(100f, planet.ScaleHeight);
			float hazeHeight = AerosolScaleHeight(planet);
			// The air and the haze still above this height, in their sea-level thicknesses.
			float air = rayleighHeight * Mathf.Exp(-heightMetres / rayleighHeight) * Mathf.Max(0f, planet.AirRelative);
			float haze = Mathf.Max(0f, aerosolExtinction) * hazeHeight * Mathf.Exp(-heightMetres / hazeHeight);
			var through = new Vector3(
				Mathf.Exp(-airMass * (RayleighSeaLevel.x * air + haze)),
				Mathf.Exp(-airMass * (RayleighSeaLevel.y * air + haze)),
				Mathf.Exp(-airMass * (RayleighSeaLevel.z * air + haze)));
			float brightest = Mathf.Max(1e-6f, Mathf.Max(through.x, Mathf.Max(through.y, through.z)));
			Vector3 colour = Vector3.Lerp(through / brightest, Vector3.one, 0.3f);
			float luminance = 0.2126f * through.x + 0.7152f * through.y + 0.0722f * through.z;
			return colour * Mathf.Pow(Mathf.Max(1e-6f, luminance), 0.45f);
		}

		/// <summary>
		/// The optical depth and diffuse transmission of an ordinary cloud over this air: what the
		/// ground under it gets.
		/// </summary>
		public static float ColumnOpticalDepth(in AirColumn column, in PlanetAir planet)
		{
			return ColumnOpticalDepth(column.ExtinctionCoefficient(planet), Mathf.Max(0f, column.Top - column.Base));
		}

		/// <summary>
		/// How much light a drop throws forward, for the tornado funnel's phase function: the drops' own
		/// asymmetry less the diffraction spike the sun's disc already draws.
		/// </summary>
		/// <remarks>
		/// Water drops scatter with an asymmetry of about 0.86, ice crystals 0.75 — but most of that is
		/// a spike of diffraction within a few degrees of the sun, which is drawn as the sun's own disc
		/// and glare. What the rest of the cloud shows is about seven tenths of it. (The clouds
		/// themselves now scatter by the full Mie phase function of their drops: see
		/// <see cref="MiePhase"/>.)
		/// </remarks>
		public static float ForwardScatter(in AirColumn column, in PlanetAir planet)
		{
			bool ice = column.SurfaceKelvin - column.MeanLapse * Mathf.Max(0f, column.Base) < AirPhysics.HomogeneousFreezeKelvin(planet.Condensate);
			return 0.7f * (ice ? 0.75f : 0.86f);
		}

		// ── The edge of a cloud ──────────────────────────────────────────

		/// <summary>
		/// The critical mixing fraction: how much of a mixture of cloud and the air round it has to be
		/// cloud for any water to be left once the mixture has evaporated what it can, 0..1.
		/// </summary>
		/// <remarks>
		/// A mixture with a share χ of cloud air holds χ·q_c of condensate and is short (1 − χ)·Δq of
		/// saturation, Δq = q_s(1 − RH) being what the outside air lacks. It stays cloudy only while the
		/// first exceeds the second: χ > χ* = Δq/(q_c + Δq) (Paluch 1979's mixing diagram; the χ_c of
		/// buoyancy sorting, Kain and Fritsch 1990). What is left is (q_c + Δq)(χ − χ*), which as a share of
		/// the cloud's own is <see cref="EdgeWater"/> — the remap every cloud renderer erodes its edges
		/// with (Schneider 2015), here given its physical threshold. Dry air around a heap needs more
		/// than nine tenths cloud air in a mixture before anything survives, so a fair-weather cumulus's
		/// edge is crisp; in saturated air every mixture keeps some, and the edge is a soft shell.
		/// </remarks>
		/// <param name="condensate">The cloud's condensate, kg per kg of air.</param>
		/// <param name="saturation">The saturation mixing ratio at the edge, kg/kg.</param>
		/// <param name="relativeHumidity">The outside air's relative humidity against the same condensate, 0..1.</param>
		public static float CriticalMixingFraction(float condensate, float saturation, float relativeHumidity)
		{
			float deficit = Mathf.Max(0f, saturation) * (1f - Mathf.Clamp01(relativeHumidity));
			float total = Mathf.Max(0f, condensate) + deficit;
			return total > 1e-12f ? Mathf.Clamp(deficit / total, 0f, MaxCritical) : 0f;
		}

		/// <summary>
		/// The most the critical fraction is allowed to be: past it the surviving water is a remap over a
		/// hundredth of the mixture, finer than the detail can draw.
		/// </summary>
		public const float MaxCritical = 0.98f;

		/// <summary>
		/// The critical mixing fraction at the edge of an ordinary cloud of the column over this air: at
		/// the middle of its depth, where most of what is seen of it is.
		/// </summary>
		public static float ColumnEdgeCritical(in AirColumn column, in PlanetAir planet)
		{
			float half = 0.5f * Mathf.Max(200f, column.Top - column.Base);
			float height = column.Base + half;
			float kelvin = column.KelvinAt(height);
			float pressure = PressureAt(planet, column, height);
			float air = pressure / (Mathf.Max(1f, planet.GasConstant) * Mathf.Max(20f, kelvin));
			float water = ColumnWater(column, half) / Mathf.Max(1e-4f, air);
			float saturation = AirPhysics.SaturationMixingRatio(kelvin, pressure, planet.GasConstant, planet.Condensate);
			return CriticalMixingFraction(water, saturation, column.RelativeHumidity);
		}

		/// <summary>
		/// The critical mixing fraction at the edge of a layer cloud, from its extinction and the size
		/// of what it is made of: condensate = ⅔·ρ·r_e·β (the inverse of <see cref="Extinction"/>).
		/// </summary>
		/// <param name="effectiveRadius">Effective radius of its drops or crystals, m.</param>
		/// <param name="ice">Whether it is ice, which evaporates into air that is humid against ice:
		/// water vapour saturated over liquid is supersaturated over ice by e_l/e_i = exp(L_f/R_v·(1/T − 1/T₀)),
		/// 1.6 at −40 °C, so the same air round a cirrus is far nearer saturation than round a drop.</param>
		public static float LayerEdgeCritical(in AirColumn column, in PlanetAir planet, float bottom, float top, float extinction, float effectiveRadius, bool ice)
		{
			float height = 0.5f * (bottom + Mathf.Max(bottom, top));
			float kelvin = column.KelvinAt(height);
			float pressure = PressureAt(planet, column, height);
			float air = pressure / (Mathf.Max(1f, planet.GasConstant) * Mathf.Max(20f, kelvin));
			float saturation = AirPhysics.SaturationMixingRatio(kelvin, pressure, planet.GasConstant, planet.Condensate);
			float humidity = column.RelativeHumidity;
			float density = AirPhysics.LiquidDensity(planet.Condensate);
			if (ice)
			{
				float over = IceSupersaturation(kelvin, planet.Condensate);
				saturation /= over;
				humidity = Mathf.Min(1f, humidity * over);
				density = planet.Condensate == Condensate.Water ? 917f : density;
			}
			float condensate = 2f / 3f * density * Mathf.Max(0f, effectiveRadius) * Mathf.Max(0f, extinction) / Mathf.Max(1e-4f, air);
			return CriticalMixingFraction(condensate, saturation, humidity);
		}

		/// <summary>
		/// How far vapour saturated over liquid is supersaturated over ice, e_l/e_i, from the latent heat
		/// of fusion (Clausius–Clapeyron for the two phases). Water only: for the other condensates the
		/// difference is not modelled and it is 1.
		/// </summary>
		public static float IceSupersaturation(float kelvin, Condensate condensate)
		{
			if (condensate != Condensate.Water)
			{
				return 1f;
			}
			const float FusionHeat = 3.34e5f;
			float t = Mathf.Max(100f, kelvin);
			float t0 = AirPhysics.FreezingKelvin(condensate);
			return t >= t0 ? 1f : Mathf.Exp(FusionHeat / AirPhysics.VapourGasConstant(condensate) * (1f / t - 1f / t0));
		}

		private static float PressureAt(in PlanetAir planet, in AirColumn column, float height)
		{
			return planet.SurfacePressure * Mathf.Exp(-Mathf.Max(0f, height) / Mathf.Max(1f, column.ScaleHeight));
		}

		/// <summary>
		/// How far out of the cut the shell's mixtures keep any water, in shells: 1 − χ* — all of the
		/// shell in saturated air, a twentieth of it in dry air — but never narrower than the pixel's
		/// cone (in shells) or a fiftieth. HLSL twin: FishCloudEdgeRampWidth.
		/// </summary>
		/// <remarks>
		/// Across the shell outside a cloud the mixtures run from pure cloud at the cut to clear air a
		/// shell out; only those more than χ* cloud keep water (<see cref="CriticalMixingFraction"/>), and
		/// what they keep rises linearly to all of it at the cut. So in dry air the edge is the cut
		/// itself, crisp; in saturated air a soft fringe a hundred metres wide stands outside it.
		/// </remarks>
		public static float EdgeRampWidth(float critical, float coneShells)
		{
			return Mathf.Max(1f - Mathf.Clamp(critical, 0f, MaxCritical), Mathf.Max(coneShells, 0.02f));
		}

		/// <summary>
		/// What water a point keeps, as a share of the cloud's own, <paramref name="inside"/> shells
		/// inside the cut (negative outside): all of it inside, falling to none a ramp's width out. HLSL
		/// twin: FishCloudEdgeWater.
		/// </summary>
		/// <remarks>
		/// The cut is where the undiluted cloud ends and the shell lies outside it, as the sinking,
		/// evaporating shell round a real cumulus does (Heus and Jonker 2008). The first version put the
		/// shell inside the cut, ramping from nothing at the cut to pure cloud a shell in: a small
		/// cumulus, narrower than a shell, never reached pure cloud anywhere, and dry air — which
		/// evaporates any mixture under nineteen parts in twenty cloud — evaporated it whole. Scattered
		/// skies went from 7 % cover to 1 %.
		/// </remarks>
		public static float EdgeWater(float inside, float ramp)
		{
			return Mathf.Clamp01(1f + inside / Mathf.Max(1e-3f, ramp));
		}

		/// <summary>
		/// What the edge keeps on average when the eddies carry it evenly over ± reach: the edge as it is
		/// seen from too far off to see the eddies, whose water it has to be or the cloud would grow or
		/// shrink as it drew away. HLSL twin: FishCloudExpectedEdgeWater.
		/// </summary>
		/// <remarks>
		/// The integral of a clamped ramp over a flat spread, in closed form: with F the antiderivative of
		/// saturate, (F(1 + (x + r)/w) − F(1 + (x − r)/w))·w/2r.
		/// </remarks>
		public static float ExpectedEdgeWater(float inside, float reach, float ramp)
		{
			float w = Mathf.Max(1e-3f, ramp);
			if (reach < 1e-4f)
			{
				return EdgeWater(inside, w);
			}
			return (SaturateIntegral(1f + (inside + reach) / w) - SaturateIntegral(1f + (inside - reach) / w)) * w / (2f * reach);
		}

		private static float SaturateIntegral(float y)
		{
			if (y <= 0f)
			{
				return 0f;
			}
			return y <= 1f ? 0.5f * y * y : y - 0.5f;
		}

		/// <summary>
		/// Which way the eddies carve, −1..1: engulfing the outside air (wisps — the clear-air pockets
		/// eaten out, the cloud left as filaments between) low in the cloud where it is drawn in at the
		/// base and the sides, and pushing cloud out (billows — rising bubbles, each with its crown)
		/// from a third of the way up. HLSL twin: FishCloudEddyPolarity.
		/// </summary>
		/// <remarks>
		/// This is Schneider's "wispy at the bottom, billowy at the top" (2015; GPU Pro 7, 2016). It was
		/// the other way round here: with the detail volume holding inverted Worley noise (high at the
		/// cell centres), eroding by 1 − noise at the base kept round cells — billows — and eroding by the
		/// noise at the top ate the cells out into a web. The comment said wispy underneath; the code
		/// drew billows there.
		/// </remarks>
		public static float EddyPolarity(float height01)
		{
			float t = Mathf.Clamp01((height01 - 0.1f) / 0.3f);
			return Mathf.Lerp(-1f, 1f, t * t * (3f - 2f * t));
		}

		/// <summary>
		/// How much of each of the detail volume's three octaves the screen can resolve, 0..1, from how
		/// wide a sample's cone is (m): an octave is drawn in full while the cone is under half its cell
		/// and gone once the cone is its cell. HLSL twin: FishCloudDetailResolved.
		/// </summary>
		/// <remarks>
		/// The octaves' cells are a quarter, an eighth and a sixteenth of the tile (the baker's Worley
		/// frequencies 4, 8 and 16). A faded octave is replaced by its mean and the rest are re-normalised
		/// by the spread that is left, so the eddies' pattern loses its fine scales with distance without
		/// the average of what it does changing (<see cref="ExpectedEdgeWater"/>). The detail used to be
		/// faded out whole between 2.5 and 11.5 km, and with it the erosion: a far cloud was thicker
		/// than the same cloud near to.
		/// </remarks>
		public static Vector3 DetailResolved(float cone, float detailTile)
		{
			float tile = Mathf.Max(1f, detailTile);
			return new Vector3(Resolved(cone, tile * 0.25f), Resolved(cone, tile * 0.125f), Resolved(cone, tile * 0.0625f));
		}

		private static float Resolved(float cone, float cell)
		{
			float t = Mathf.Clamp01((Mathf.Max(0f, cone) / cell - 0.5f) / 0.5f);
			return 1f - t * t * (3f - 2f * t);
		}

		/// <summary>
		/// The periods the detail's drift is wrapped to, m, along the wind and across it: one tile across,
		/// and one tile times the stretch along, since the lookup divides the along-wind metres by the
		/// stretch — so a wrap moves the lookup by exactly one whole tile, and the pattern never jumps
		/// (the 42 × k rule, for the detail).
		/// </summary>
		public static Vector2 DetailDriftPeriod(float detailScale, float stretch)
		{
			float tile = Mathf.Max(20f, detailScale);
			return new Vector2(tile * Mathf.Max(1f, Mathf.Round(stretch)), tile);
		}
	}
}
