#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Water;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>The ice a scene's sea holds, worked out from its climate before anything is placed.</summary>
	public struct IceClimate
	{
		/// <summary>Why there is no ice, when there is none; null when there is some.</summary>
		public string NoIceReason;
		/// <summary>Sea-level air temperature at the scene, °C: the year's mean, coldest and warmest season.</summary>
		public float AirMeanC, AirColdestC, AirWarmestC;
		/// <summary>Annual mean sea surface temperature, °C: the air's mean, never below sea water's freezing point.</summary>
		public float SeaSurfaceC;
		/// <summary>Year-averaged sea ice concentration, 0..1 (WMO tenths over ten).</summary>
		public float SeaIceConcentration;
		/// <summary>True when a glaciated coast that calves bergs lies within drift range.</summary>
		public bool HasCalvingSource;
		/// <summary>Degrees of arc from the scene to that coast; 0 when it is the scene's own.</summary>
		public float DriftDegrees;
		/// <summary>Annual mean sea-level air temperature at that coast, °C: below −5 it can hold ice shelves.</summary>
		public float SourceAnnualC;
		/// <summary>Icebergs per unit of the abundance table here, 0..1: survival in this water times the drift's toll.</summary>
		public float BergSupply;
		/// <summary>The largest IIP size class that reaches this water before it melts; null for none.</summary>
		public IcebergSize? LargestBerg;
		/// <summary>
		/// True when the scene's own coast is the calving front: the source is its own (no drift) and its
		/// summer never thaws, so the ice sheet meets this sea (<see cref="IceSheetSurface"/>).
		/// </summary>
		public bool CalvingFront;

		public bool HasSeaIce => NoIceReason == null && SeaIceConcentration > 0f;
		public bool HasBergs => NoIceReason == null && BergSupply > 0f && LargestBerg.HasValue;
		public bool AnyIce => HasSeaIce || HasBergs;

		public static IceClimate None(string reason) => new IceClimate { NoIceReason = reason };

		public override string ToString()
		{
			if (NoIceReason != null)
			{
				return NoIceReason;
			}
			string bergs = HasCalvingSource
				? $"calving coast {DriftDegrees:0.#}° away (annual {SourceAnnualC:0.#} °C{(SourceAnnualC <= IceOccurrence.IceShelfLimitC ? ", ice shelves" : string.Empty)}), " +
				  $"berg supply {BergSupply:0.00}, largest {(LargestBerg.HasValue ? LargestBerg.Value.ToString() : "none")}"
				: "no calving coast within drift range";
			return $"air {AirMeanC:0.#} °C (coldest {AirColdestC:0.#}, warmest {AirWarmestC:0.#}), sea {SeaSurfaceC:0.#} °C, " +
				$"sea ice {SeaIceConcentration:0.00}; {bergs}{(CalvingFront ? ", calving front here" : string.Empty)}";
		}
	}

	/// <summary>
	/// Where floating ice occurs and how much: pure functions of temperature and distance, so the
	/// rules are testable without a planet.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Sea ice is made where the sea freezes.</b> Sea water of ordinary salinity (about 34–35)
	/// freezes at about −1.8 °C (the UNESCO/TEOS-10 freezing point; fresh water's 0 °C does not
	/// apply), and the sea surface cannot be colder than that — so the sea surface temperature is the
	/// air's annual mean held at −1.8, and ice forms in the seasons whose air is below it. The pack
	/// thickens and closes as the cold deepens: concentration ramps from nothing at −1.8 to a closed
	/// pack <see cref="PackRampKelvin"/> colder, and is averaged over the year because a scene is
	/// placed once and stands all year. At the ice edge, where waves still reach, new ice forms as
	/// pancakes (WMO Sea-Ice Nomenclature, WMO-No. 259: pancake ice forms from grease ice or slush in
	/// a swell); deeper in the pack the floes are larger.
	/// </para>
	/// <para>
	/// <b>Icebergs are glacier ice, carried.</b> They need a calving source — a coast whose glaciers
	/// reach the sea, taken here as land beside the sea with its sea-level annual mean at or below
	/// <see cref="GlacierCoastC"/> — and they drift equatorward from it, as the Greenland and
	/// Antarctic bergs do, up to <see cref="MaxDriftDegrees"/> of latitude. Melt rises steeply with
	/// the water's excess over freezing (Bigg et al. 1997, Cold Regions Sci. Tech. 26: buoyant
	/// convection, forced convection and wave erosion all grow with the sea surface temperature), so
	/// supply falls and the largest survivor shrinks as the water warms, to none past
	/// <see cref="BergSurvivalC"/>, and every step of drift costs supply and size too.
	/// </para>
	/// <para>
	/// <b>The shape follows the source.</b> Tabular bergs calve from ice shelves, and ice shelves
	/// exist only where the annual mean is below about −5 °C (Morris &amp; Vaughan 2003, the thermal
	/// limit of ice-shelf viability on the Antarctic Peninsula); outlet glaciers calve blocky and
	/// wedge-shaped bergs; and erosion in warmer water carves domes, pinnacles and drydocks (the
	/// International Ice Patrol's shape classes, the same ones <see cref="IceMeshes.Icebergs"/> is
	/// built from).
	/// </para>
	/// </remarks>
	public static class IceOccurrence
	{
		/// <summary>Freezing point of sea water of salinity ~34–35 at the surface, °C.</summary>
		public const float SeaWaterFreezingC = -1.8f;
		/// <summary>Warmest annual mean sea surface an iceberg survives into, °C.</summary>
		public const float BergSurvivalC = 5f;
		/// <summary>Kelvin of cold below sea water's freezing point over which the pack goes from none to closed.</summary>
		public const float PackRampKelvin = 6f;
		/// <summary>Warmest sea-level annual mean at which a coast's glaciers are taken to reach the sea, °C.</summary>
		public const float GlacierCoastC = 0f;
		/// <summary>The annual mean below which ice shelves survive, °C (Morris &amp; Vaughan 2003).</summary>
		public const float IceShelfLimitC = -5f;
		/// <summary>Furthest a berg is followed from its source, degrees of arc.</summary>
		public const float MaxDriftDegrees = 30f;
		/// <summary>Degrees of drift that cost a berg supply a factor of e.</summary>
		public const float DriftScaleDegrees = 12f;
		/// <summary>Degrees of drift that cost one IIP size class.</summary>
		public const float DriftPerSizeDegrees = 10f;
		/// <summary>Seasons the year is sampled at.</summary>
		public const int Seasons = 12;

		/// <summary>
		/// Bergs per km² of open sea at a supply of 1, by IIP size class. A gameplay calibration, not a
		/// measurement: fragments outnumber their parents, and a small scene sees a large berg only rarely.
		/// </summary>
		public static float BergsPerKm2(IcebergSize size)
		{
			return BergsPerKm2(size, false);
		}

		/// <summary>
		/// How many times the drifting abundance a calving front's own water holds, by size: the fragments a
		/// face sheds (growlers, bergy bits, brash) choke the water in front of it for kilometres, while the big
		/// bergs are no commoner there than anywhere they drift to.
		/// </summary>
		public static float CalvingFrontBoost(IcebergSize size)
		{
			switch (size)
			{
				case IcebergSize.Growler: return 8f;
				case IcebergSize.BergyBit: return 6f;
				case IcebergSize.Small: return 3f;
				default: return 1f;
			}
		}

		/// <summary>Bergs per km² at a supply of 1, in front of a calving face when <paramref name="calvingFront"/>.</summary>
		public static float BergsPerKm2(IcebergSize size, bool calvingFront)
		{
			return BaseBergsPerKm2(size) * (calvingFront ? CalvingFrontBoost(size) : 1f);
		}

		private static float BaseBergsPerKm2(IcebergSize size)
		{
			switch (size)
			{
				case IcebergSize.Growler: return 4f;
				case IcebergSize.BergyBit: return 2f;
				case IcebergSize.Small: return 0.5f;
				case IcebergSize.Medium: return 0.1f;
				default: return 0.02f;
			}
		}

		/// <summary>Annual mean sea surface temperature from the sea-level air's, °C.</summary>
		public static float SeaSurfaceC(float airMeanC) => Mathf.Max(SeaWaterFreezingC, airMeanC);

		/// <summary>Sea ice concentration while the sea-level air is at a temperature, 0..1.</summary>
		public static float SeaIceConcentration(float airC) => Mathf.Clamp01((SeaWaterFreezingC - airC) / PackRampKelvin);

		/// <summary>Year-averaged sea ice concentration over seasonal air temperatures, 0..1.</summary>
		public static float SeaIceConcentration(IReadOnlyList<float> seasonsC)
		{
			if (seasonsC == null || seasonsC.Count == 0)
			{
				return 0f;
			}
			float sum = 0f;
			for (int i = 0; i < seasonsC.Count; i++)
			{
				sum += SeaIceConcentration(seasonsC[i]);
			}
			return sum / seasonsC.Count;
		}

		/// <summary>Share of sea ice pieces that are pancakes: nearly all at the ice edge, few in the closed pack.</summary>
		public static float PancakeShare(float concentration) => Mathf.Lerp(0.9f, 0.1f, Mathf.InverseLerp(0.15f, 0.7f, concentration));

		/// <summary>How many of a source's bergs survive into water of this temperature, 0..1.</summary>
		/// <remarks>Squared: melt grows with the water's warmth and a berg's lifetime falls with it, so the toll compounds.</remarks>
		public static float BergSurvival(float seaSurfaceC)
		{
			float x = Mathf.Clamp01((BergSurvivalC - seaSurfaceC) / (BergSurvivalC - SeaWaterFreezingC));
			return x * x;
		}

		/// <summary>Berg supply at a sea: survival here times the toll of the drift from the source; 0 with no source.</summary>
		public static float BergSupply(float seaSurfaceC, bool hasSource, float driftDegrees)
		{
			if (!hasSource || driftDegrees > MaxDriftDegrees || seaSurfaceC > BergSurvivalC)
			{
				return 0f;
			}
			return BergSurvival(seaSurfaceC) * Mathf.Exp(-Mathf.Max(0f, driftDegrees) / DriftScaleDegrees);
		}

		/// <summary>
		/// The largest IIP size class that reaches this water: smaller as it warms, and one class smaller
		/// for every <see cref="DriftPerSizeDegrees"/> of drift. Null when none survives.
		/// </summary>
		public static IcebergSize? LargestBergSize(float seaSurfaceC, float driftDegrees)
		{
			IcebergSize size;
			if (seaSurfaceC <= 0f) size = IcebergSize.Large;
			else if (seaSurfaceC <= 2f) size = IcebergSize.Medium;
			else if (seaSurfaceC <= 3.5f) size = IcebergSize.Small;
			else if (seaSurfaceC <= 4.5f) size = IcebergSize.BergyBit;
			else if (seaSurfaceC <= BergSurvivalC) size = IcebergSize.Growler;
			else return null;
			int steps = Mathf.FloorToInt(Mathf.Max(0f, driftDegrees) / DriftPerSizeDegrees);
			return (IcebergSize)Mathf.Max((int)IcebergSize.Growler, (int)size - steps);
		}

		/// <summary>
		/// Relative weight of an IIP shape class among the bergs of one size: tabular only from an
		/// ice-shelf coast and mostly near it; fresh calving blocky and wedge-shaped; domes, pinnacles and
		/// drydocks the more the water and the drift have eroded them.
		/// </summary>
		public static float ClassWeight(IcebergClass shape, float sourceAnnualC, float seaSurfaceC, float driftDegrees)
		{
			float erosion = Mathf.Clamp01(Mathf.Clamp01((seaSurfaceC - SeaWaterFreezingC) / (BergSurvivalC - SeaWaterFreezingC)) + driftDegrees / MaxDriftDegrees);
			float near = Mathf.Exp(-Mathf.Max(0f, driftDegrees) / DriftScaleDegrees);
			switch (shape)
			{
				case IcebergClass.Tabular: return sourceAnnualC <= IceShelfLimitC ? 3f * near * (1f - 0.6f * erosion) : 0f;
				case IcebergClass.Blocky: return 1f - 0.6f * erosion;
				case IcebergClass.Wedge: return 1f - 0.4f * erosion;
				case IcebergClass.Dome: return 0.5f + erosion;
				case IcebergClass.Pinnacle: return 0.5f + erosion;
				case IcebergClass.Drydock: return 0.2f + 1.2f * erosion;
				default: return 1f;
			}
		}

		/// <summary>Everything about a scene's ice from its seasonal air temperatures and its berg source.</summary>
		public static IceClimate Decide(IReadOnlyList<float> airSeasonsC, bool hasSource, float driftDegrees, float sourceAnnualC)
		{
			if (airSeasonsC == null || airSeasonsC.Count == 0)
			{
				return IceClimate.None("Sea ice: no climate to read.");
			}
			float mean = 0f, coldest = float.MaxValue, warmest = float.MinValue;
			foreach (float t in airSeasonsC)
			{
				mean += t;
				coldest = Mathf.Min(coldest, t);
				warmest = Mathf.Max(warmest, t);
			}
			mean /= airSeasonsC.Count;
			float sst = SeaSurfaceC(mean);
			var climate = new IceClimate
			{
				AirMeanC = mean,
				AirColdestC = coldest,
				AirWarmestC = warmest,
				SeaSurfaceC = sst,
				SeaIceConcentration = SeaIceConcentration(airSeasonsC),
				HasCalvingSource = hasSource && driftDegrees <= MaxDriftDegrees,
				DriftDegrees = hasSource ? driftDegrees : 0f,
				SourceAnnualC = hasSource ? sourceAnnualC : 0f,
			};
			climate.BergSupply = BergSupply(sst, climate.HasCalvingSource, driftDegrees);
			climate.LargestBerg = climate.BergSupply > 0f ? LargestBergSize(sst, driftDegrees) : null;
			if (!climate.AnyIce)
			{
				climate.NoIceReason = $"Sea ice: none — the sea is {sst:0.#} °C (air {mean:0.#} °C)" +
					(climate.HasCalvingSource ? ", too warm for bergs to reach." : ", and no calving coast lies within drift range.");
			}
			return climate;
		}

		// ── From a planet ─────────────────────────────────────────────

		/// <summary>Sea-level air temperature at a point over the year, °C, one per season.</summary>
		public static float[] SeasonalAirC(in PlanetClimateField field, SolarSystemProfile system, WorldBody body, double latitude, Vector3 direction)
		{
			var seasons = new float[Seasons];
			double year = CelestialMath.OrbitHours(system, CelestialMath.HostPlanet(body));
			if (double.IsNaN(year) || double.IsInfinity(year) || year <= 0.0)
			{
				year = 0.0;
			}
			float regional = field.RegionalOffset(direction);
			for (int s = 0; s < Seasons; s++)
			{
				double hours = year * s / Seasons;
				float scale = Mathf.Clamp(field.SubSolarTemperature
					+ (float)CelestialMath.LatitudeTemperature(system, body, hours, latitude)
					+ regional, -1f, 1f);
				seasons[s] = (float)(scale * ClimateModel.KelvinPerUnit);
			}
			return seasons;
		}

		private static float Mean(float[] values)
		{
			float sum = 0f;
			foreach (float v in values)
			{
				sum += v;
			}
			return values.Length > 0 ? sum / values.Length : 0f;
		}

		/// <summary>
		/// The nearest calving coast poleward of a point, within <see cref="MaxDriftDegrees"/>: land beside
		/// the sea whose sea-level annual mean is at or below <see cref="GlacierCoastC"/>.
		/// </summary>
		/// <remarks>
		/// Walked in rings of latitude toward the nearer pole (both, on the equator), each ring sampled
		/// across ±18° of longitude for the currents that carry bergs sideways as they go. A point is
		/// coast when one of its four neighbours <see cref="CoastProbeDegrees"/> away is sea. About a
		/// thousand surface samples: cheap beside the terrain itself.
		/// </remarks>
		public static bool FindCalvingSource(in PlanetClimateField field, SolarSystemProfile system, WorldBody body,
			double latitude, double longitude, out float driftDegrees, out float sourceAnnualC)
		{
			driftDegrees = float.MaxValue;
			sourceAnnualC = 0f;
			if (body == null)
			{
				return false;
			}
			uint seed = body.ResolvedTerrainSeed;
			int[] hemispheres = Math.Abs(latitude) < 1.0 ? new[] { 1, -1 } : new[] { Math.Sign(latitude) };
			double[] offsets = { 0.0, -6.0, 6.0, -12.0, 12.0, -18.0, 18.0 };
			foreach (int sign in hemispheres)
			{
				for (float step = 0f; step <= MaxDriftDegrees; step += SourceStepDegrees)
				{
					double ringLatitude = Math.Max(-89.0, Math.Min(89.0, latitude + sign * step));
					foreach (double offset in offsets)
					{
						double lon = longitude + offset;
						Vector3 direction = PlanetSurface.Direction(ringLatitude, lon);
						if (PlanetSurface.AltitudeMetres(seed, body, direction) <= 0f)
						{
							continue;
						}
						// At sea level: the calving front is where the glacier meets the sea.
						float annual = Mean(SeasonalAirC(field, system, body, ringLatitude, direction));
						if (annual > GlacierCoastC || !IsCoast(seed, body, ringLatitude, lon))
						{
							continue;
						}
						float across = (float)(Math.Abs(offset) * Math.Cos(ringLatitude * Math.PI / 180.0));
						float drift = Mathf.Sqrt(step * step + across * across);
						if (drift < driftDegrees)
						{
							driftDegrees = drift;
							sourceAnnualC = annual;
						}
					}
					if (driftDegrees <= step + SourceStepDegrees)
					{
						break;
					}
				}
			}
			return driftDegrees <= MaxDriftDegrees;
		}

		/// <summary>Degrees between the rings the source search walks.</summary>
		public const float SourceStepDegrees = 1.5f;

		/// <summary>How far from a land point the source search looks for the sea beside it, degrees.</summary>
		public const double CoastProbeDegrees = 0.75;

		private static bool IsCoast(uint seed, WorldBody body, double latitude, double longitude)
		{
			return PlanetSurface.AltitudeMetres(seed, body, PlanetSurface.Direction(Math.Min(89.0, latitude + CoastProbeDegrees), longitude)) <= 0f
				|| PlanetSurface.AltitudeMetres(seed, body, PlanetSurface.Direction(Math.Max(-89.0, latitude - CoastProbeDegrees), longitude)) <= 0f
				|| PlanetSurface.AltitudeMetres(seed, body, PlanetSurface.Direction(latitude, longitude + CoastProbeDegrees)) <= 0f
				|| PlanetSurface.AltitudeMetres(seed, body, PlanetSurface.Direction(latitude, longitude - CoastProbeDegrees)) <= 0f;
		}

		/// <summary>The ice a generated scene's sea holds, from its body's climate at the scene's centre.</summary>
		public static IceClimate ForScene(SceneGenerationRequest request)
		{
			if (request == null || request.Body == null)
			{
				return IceClimate.None("Sea ice: no body to read a climate from.");
			}
			if (request.FrozenSeas)
			{
				// The Ice Shelf biome lays a frozen-through sea as terrain; floating ice on it would be a second copy.
				return IceClimate.None("Sea ice: none placed — this world's seas are frozen through, which the Ice Shelf ground already is.");
			}
			SolarSystemProfile system = SolarSystemProfile.Resolve(request.Body);
			if (SurfaceLiquids.For(system, request.Body) != SurfaceLiquid.Water)
			{
				return IceClimate.None("Sea ice: none — the surface liquid is not water.");
			}
			PlanetClimateField field = PlanetClimateField.For(system, request.Body);
			Vector3 direction = AtlasGeometry.SceneToUnit(request.Footprint, 0.0, 0.0, request.ResolvedRadiusKm).ToVector3().normalized;
			double latitude = PlanetClimateField.LatitudeOf(direction);
			float[] seasons = SeasonalAirC(field, system, request.Body, latitude, direction);
			bool source = FindCalvingSource(field, system, request.Body, latitude, request.Longitude, out float drift, out float sourceC);
			IceClimate climate = Decide(seasons, source, drift, sourceC);
			// Its own coast calving: the source is here and no month thaws, so the ice sheet reaches this sea.
			climate.CalvingFront = climate.HasCalvingSource && drift < SourceStepDegrees && climate.AirWarmestC <= 0f
				&& BiomeRegistry.Contains(IceSheetSurface.BiomeName);
			return climate;
		}
	}

	/// <summary>Tuning for <see cref="IcePlacer.Place(Scene, IceSite, IceClimate, WaterShoreField.Snapshot, uint, IIcePrefabSource, IcePlacerOptions)"/>; the defaults are the shipped behaviour.</summary>
	public sealed class IcePlacerOptions
	{
		/// <summary>Clear water kept between a berg's footprint circle and any other piece's, m.</summary>
		public float BergGapMetres = 15f;
		/// <summary>Clear water kept between two pieces of sea ice, m: wider than a swimmer, so no piece pair walls one in.</summary>
		public float SeaIceGapMetres = 3f;
		/// <summary>Least distance from any point of a berg's footprint to the mean waterline, m.</summary>
		public float BergShoreGapMetres = 25f;
		/// <summary>Least distance from any point of a floe's footprint to the mean waterline, m.</summary>
		public float SeaIceShoreGapMetres = 6f;
		/// <summary>Distance kept between a piece's footprint and the scene's edge (its boundary), m.</summary>
		public float EdgeMarginMetres = 60f;
		/// <summary>Least openness to the sea's waves (<see cref="WaterShoreField.Snapshot.OpenSeaAt"/>) at a piece's centre.</summary>
		public float MinOpenSea = 0.5f;
		/// <summary>Water kept under the keel beyond the low tide and the heave, m.</summary>
		public float KeelMarginMetres = 0.5f;
		/// <summary>How far a floating piece may heave down with the waves, m.</summary>
		public float HeaveAllowanceMetres = 1f;
		/// <summary>How far a grounded piece may stand above its float line, as a share of its draught.</summary>
		public float MaxGroundedRise = 0.1f;
		/// <summary>Most of the open sea sea ice may cover, whatever the concentration: the rest stays navigable.</summary>
		public float MaxSeaIceCoverage = 0.35f;
		/// <summary>Most sea ice pieces in one scene.</summary>
		public int MaxSeaIcePieces = 600;
		/// <summary>Most icebergs of all sizes in one scene.</summary>
		public int MaxBergs = 150;
		/// <summary>Spacing of the grid the open sea is measured on, m.</summary>
		public float SeaSampleMetres = 16f;
		/// <summary>Most samples along each side of a footprint.</summary>
		public int FootprintSamples = 9;
		/// <summary>Positions tried for each piece before it is given up.</summary>
		public int AttemptsPerPiece = 40;
		/// <summary>Spatial hash cell for the spacing test, m.</summary>
		public float CellMetres = 64f;

		/// <summary>
		/// Where the scene-level entry points get their prefabs; null uses <see cref="ProjectIcePrefabs"/>.
		/// </summary>
		/// <remarks>
		/// The seam a LOCAL scene's prefab overrides come through (<see cref="LocalArtScope.IceOptions"/>);
		/// left null for every scene outside Assets/LOCAL, so committed scenes only ever instance the
		/// generated prefabs. The overload taking an <see cref="IIcePrefabSource"/> directly ignores it.
		/// </remarks>
		public IIcePrefabSource Prefabs;
	}

	/// <summary>Where the sea is in the scene the ice goes into.</summary>
	public struct IceSite
	{
		/// <summary>The scene's rectangle in world XZ: its boundary.</summary>
		public Rect Area;
		/// <summary>World Y of mean sea level.</summary>
		public float SeaLevelY;
		/// <summary>How far below mean sea level the lowest tide goes, m.</summary>
		public float LowTideMetres;
		/// <summary>The sea the floaters ride; null lets each find it.</summary>
		public WaterSurface Surface;
	}

	/// <summary>Where the placer gets its prefabs: referenced, never made.</summary>
	public interface IIcePrefabSource
	{
		/// <summary>The prefab's whole body in its own space (pivot on the waterline), or false when it has not been generated.</summary>
		bool TryMeasure(string prefabName, out Bounds localBounds);
		/// <summary>An instance of the prefab — linked to it, not a copy — under the parent.</summary>
		GameObject Instantiate(string prefabName, Transform parent);
	}

	/// <summary>The art generator's prefabs in the project, through <see cref="PrefabUtility.InstantiatePrefab(UnityEngine.Object, Transform)"/>.</summary>
	public sealed class ProjectIcePrefabs : IIcePrefabSource
	{
		private readonly Dictionary<string, GameObject> loaded = new Dictionary<string, GameObject>(StringComparer.Ordinal);

		public static string PrefabPath(string prefabName) => ProceduralArtCatalogue.PrefabPath(prefabName);

		private GameObject Load(string prefabName)
		{
			if (!loaded.TryGetValue(prefabName, out GameObject prefab) || prefab == null)
			{
				prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(prefabName));
				loaded[prefabName] = prefab;
			}
			return prefab;
		}

		public bool TryMeasure(string prefabName, out Bounds localBounds)
		{
			localBounds = default;
			GameObject prefab = Load(prefabName);
			if (prefab == null)
			{
				return false;
			}
			/* The root's collider mesh is the whole body (LOD1), pivot on the waterline: its bounds
			 * carry the draught below y = 0 and the footprint, ram included. The renderers otherwise. */
			MeshCollider collider = prefab.GetComponent<MeshCollider>();
			if (collider != null && collider.sharedMesh != null)
			{
				localBounds = collider.sharedMesh.bounds;
				return true;
			}
			bool any = false;
			foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
			{
				// A prefab asset's root stands at the origin, so its renderers' world bounds are its own.
				if (!any)
				{
					localBounds = renderer.bounds;
					any = true;
				}
				else
				{
					localBounds.Encapsulate(renderer.bounds);
				}
			}
			return any;
		}

		public GameObject Instantiate(string prefabName, Transform parent)
		{
			GameObject prefab = Load(prefabName);
			return prefab != null ? PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject : null;
		}
	}

	/// <summary>Whether a piece floats where it was tried, sits on the sea bed, or cannot go there.</summary>
	public enum IceFooting
	{
		Floats,
		Grounded,
		Rejected,
	}

	/// <summary>What <see cref="IcePlacer"/> did.</summary>
	public sealed class IcePlacerReport
	{
		public IceClimate Climate;
		/// <summary>Problems and things a person should know, one line each, for the generation result's notes.</summary>
		public readonly List<string> Notes = new List<string>();
		/// <summary>Pieces placed per prefab name.</summary>
		public readonly SortedDictionary<string, int> Placed = new SortedDictionary<string, int>(StringComparer.Ordinal);
		/// <summary>Pieces wanted but left out because their prefab has not been generated, per prefab name.</summary>
		public readonly SortedDictionary<string, int> Missing = new SortedDictionary<string, int>(StringComparer.Ordinal);
		/// <summary>Pieces wanted but for which no position passed, per prefab name.</summary>
		public readonly SortedDictionary<string, int> NoRoom = new SortedDictionary<string, int>(StringComparer.Ordinal);
		/// <summary>Candidate positions turned down, by reason.</summary>
		public readonly SortedDictionary<string, int> Rejections = new SortedDictionary<string, int>(StringComparer.Ordinal);
		public int Bergs, SeaIce, Grounded;
		/// <summary>Open sea the ice could go on, km².</summary>
		public float SeaKm2;
		/// <summary>True when the concentration asked for more sea ice than the coverage or piece cap allows.</summary>
		public bool SeaIceCapped;
		/// <summary>The placed root; null when nothing was placed.</summary>
		public GameObject Root;

		public int MissingAssets
		{
			get
			{
				int n = 0;
				foreach (int v in Missing.Values) n += v;
				return n;
			}
		}

		public override string ToString()
		{
			var sb = new StringBuilder("[Sea ice] ");
			sb.Append(Bergs).Append(" berg(s), ").Append(SeaIce).Append(" sea ice piece(s), ").Append(Grounded).Append(" grounded on ")
				.Append(SeaKm2.ToString("0.00")).Append(" km² of open sea");
			if (Placed.Count > 0)
			{
				sb.Append(" [");
				bool first = true;
				foreach (KeyValuePair<string, int> kv in Placed)
				{
					sb.Append(first ? string.Empty : ", ").Append(kv.Key).Append(' ').Append(kv.Value);
					first = false;
				}
				sb.Append(']');
			}
			sb.Append("; ").Append(Climate.ToString());
			if (MissingAssets > 0)
			{
				sb.Append("; ").Append(MissingAssets).Append(" left out for missing prefabs");
			}
			return sb.Append('.').ToString();
		}
	}

	/// <summary>
	/// Puts icebergs, bergy bits, growlers and sea ice on a generated scene's sea where its climate
	/// makes them: one "Sea Ice" root, every piece a linked instance of the art generator's prefab
	/// riding the waves on its <see cref="WaterFloater"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Where it runs.</b> From <c>SceneGenerator.AddWater</c>, after the sea's components are on the
	/// Water host, for liquid water only — never lava, never a frozen-through world, whose sea the Ice
	/// Shelf ground already is. The climate (<see cref="IceOccurrence"/>) is decided first; only a
	/// scene that has ice pays for the shore field to be built on the spot.
	/// </para>
	/// <para>
	/// <b>Idempotent.</b> Every root of the scene named <see cref="RootName"/> and carrying
	/// <see cref="GeneratedSeaIce"/> is destroyed before anything is placed, and at most one is made.
	/// </para>
	/// <para>
	/// <b>Never through the sea bed.</b> A piece's keel is taken as a half-ellipsoid of its measured
	/// draught over its measured footprint (ram included), and the shore field's depth is sampled
	/// across the whole footprint, rotated as placed. It floats only where every sample clears the
	/// keel at the lowest tide, with the heave and a margin besides. Otherwise it may stand grounded —
	/// static, its floater's <see cref="WaterFloater.Response"/> at 0, raised onto the bed by at most
	/// <see cref="IcePlacerOptions.MaxGroundedRise"/> of its draught — or it is not placed there.
	/// </para>
	/// <para>
	/// <b>Never a trap.</b> Pieces keep a gap from the waterline (<see cref="IcePlacerOptions.BergShoreGapMetres"/>),
	/// from the scene's edge, and from each other: every pair's footprint circles stay at least the
	/// gap apart, so the circles grown by half the gap are disjoint, and the water outside a set of
	/// disjoint discs is connected — anything narrower than the gap can swim between any two pieces
	/// and along every shore. Sea ice never covers more than <see cref="IcePlacerOptions.MaxSeaIceCoverage"/>
	/// of the sea, whatever the concentration, and goes only where the sea's waves reach
	/// (<see cref="WaterShoreField.Snapshot.OpenSeaAt"/>): ice drifts in from the open sea, never into a pool.
	/// </para>
	/// <para>
	/// <b>Deterministic.</b> Everything random comes from the scene's seed, in a fixed order — sizes
	/// largest first, then floes, then pancakes — so the same scene places the same ice.
	/// </para>
	/// <para>
	/// <b>Assets are referenced, never made.</b> Prefabs are <c>Iceberg_{Class}{Size}</c> and
	/// <c>SeaIce_{Name}</c> from the art generator (<see cref="IceMeshes"/>), instantiated linked. A
	/// prefab that has not been generated is skipped and counted in the notes.
	/// </para>
	/// </remarks>
	public static class IcePlacer
	{
		/// <summary>The one root every placed piece lives under.</summary>
		public const string RootName = "Sea Ice";

		/// <summary>The art generator's prefab for a berg: <c>Iceberg_{Class}{Size}</c>.</summary>
		public static string IcebergPrefab(in IcebergShape shape) => "Iceberg_" + shape.Name;

		/// <summary>The art generator's prefab for a piece of sea ice: <c>SeaIce_{Name}</c>.</summary>
		public static string SeaIcePrefab(in SeaIceShape shape) => "SeaIce_" + shape.Name;

		/// <summary>Removes every root this placer made in the scene; returns how many.</summary>
		public static int Clear(Scene scene)
		{
			int removed = 0;
			if (!scene.IsValid())
			{
				return 0;
			}
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (root != null && root.name == RootName && root.GetComponent<GeneratedSeaIce>() != null)
				{
					UnityEngine.Object.DestroyImmediate(root);
					removed++;
				}
			}
			return removed;
		}

		/// <summary>
		/// Places a generated scene's sea ice: decides the climate, builds the shore field if there is
		/// ice to place, and places it on the Water host's sea.
		/// </summary>
		/// <param name="scene">The scene the root goes into.</param>
		/// <param name="request">What the scene was cut from: the body and where on it.</param>
		/// <param name="waterHost">The "Water" object: its <see cref="WaterSurface"/> and sea level.</param>
		/// <param name="shoreField">The sea's depth field; built here, synchronously, if it is not yet.</param>
		/// <param name="seed">The scene's seed.</param>
		/// <param name="plan">The scene's tiles, for its rectangle.</param>
		public static IcePlacerReport Place(Scene scene, SceneGenerationRequest request, GameObject waterHost, WaterShoreField shoreField,
			uint seed, TerrainTilePlan plan, IcePlacerOptions options = null)
		{
			WaterSurface surface = waterHost != null ? waterHost.GetComponent<WaterSurface>() : null;
			IceClimate climate = surface != null && surface.IsLava
				? IceClimate.None("Sea ice: none on lava.")
				: IceOccurrence.ForScene(request);
			WaterShoreField.Snapshot snapshot = null;
			if (climate.AnyIce && shoreField != null)
			{
				snapshot = shoreField.Current;
				if (snapshot == null || shoreField.IsBuilding)
				{
					// The live build started when the component was added runs off the main thread; this one finishes here.
					shoreField.Build();
					snapshot = shoreField.Current;
				}
			}
			float lowTide = shoreField != null ? shoreField.HighestTide : 0f;
			WaterEnvironment environment = waterHost != null ? waterHost.GetComponent<WaterEnvironment>() : null;
			if (environment != null)
			{
				lowTide = Mathf.Max(lowTide, environment.MaximumTideMetres);
			}
			var site = new IceSite
			{
				Area = new Rect(-plan.WidthMetres * 0.5f, -plan.DepthMetres * 0.5f, plan.WidthMetres, plan.DepthMetres),
				SeaLevelY = waterHost != null ? waterHost.transform.position.y : 0f,
				LowTideMetres = lowTide,
				Surface = surface,
			};
			IcePlacerReport report = Place(scene, site, climate, snapshot, seed, options?.Prefabs ?? new ProjectIcePrefabs(), options);
			if (report.Root != null && !Application.isBatchMode && scene.IsValid() && !EditorSceneManager.IsPreviewScene(scene))
			{
				EditorSceneManager.MarkSceneDirty(scene);
			}
			return report;
		}

		/// <summary>
		/// The scene's own seed, exactly as <c>SceneGenerator.SceneSeed</c> makes it (its body's terrain
		/// seed mixed with its name), for tools that place ice outside a generation run.
		/// </summary>
		/// <remarks>Pinned equal to the generator's by <c>IcePlacerTests</c>, so a repaint places the ice a generation did.</remarks>
		public static uint SceneSeed(SceneGenerationRequest request)
		{
			uint seed = request != null && request.Body != null ? request.Body.ResolvedTerrainSeed : 1u;
			return seed ^ unchecked((uint)(request?.SceneName ?? string.Empty).GetDeterministicHashCode());
		}

		/// <summary>
		/// Places the ice of a scene that is already open and already has its sea — for tools that
		/// repaint a generated scene without cutting it again (the biome repaint).
		/// </summary>
		/// <remarks>
		/// Finds the scene's <see cref="WaterSurface"/> host and its <see cref="WaterShoreField"/> and runs
		/// the same placement as a generation. Skips entirely, touching nothing and saying why, when the
		/// scene has no sea or its liquid is lava. Idempotent like the rest: the marked root is replaced.
		/// </remarks>
		public static IcePlacerReport PlaceInOpenScene(Scene scene, SceneGenerationRequest request, uint seed, IcePlacerOptions options = null)
		{
			WaterSurface surface = null;
			if (scene.IsValid())
			{
				foreach (GameObject root in scene.GetRootGameObjects())
				{
					surface = root != null ? root.GetComponentInChildren<WaterSurface>(true) : null;
					if (surface != null)
					{
						break;
					}
				}
			}
			if (surface == null)
			{
				var none = new IcePlacerReport { Climate = IceClimate.None("Sea ice: the scene has no sea, so none was placed.") };
				none.Notes.Add(none.Climate.NoIceReason);
				return none;
			}
			if (surface.IsLava)
			{
				var lava = new IcePlacerReport { Climate = IceClimate.None("Sea ice: the scene's liquid is lava, so none was placed.") };
				lava.Notes.Add(lava.Climate.NoIceReason);
				return lava;
			}
			TerrainTilePlan plan = SceneGeneration.PlanTiles(request != null ? request.SizeKm : Vector2.zero);
			return Place(scene, request, surface.gameObject, surface.GetComponent<WaterShoreField>(), seed, plan, options);
		}

		/// <summary>One planned piece, before it has a place.</summary>
		private struct Planned
		{
			public string Prefab;
			public bool Berg;
		}

		/// <summary>A placed piece's footprint circle, for the spacing test.</summary>
		private struct Disc
		{
			public Vector2 Centre;
			public float Radius;
			public bool Berg;
		}

		/// <summary>
		/// Places ice on a sea described by a depth field: the testable core. Replaces this placer's
		/// previous root; leaves none when there is nothing to place.
		/// </summary>
		public static IcePlacerReport Place(Scene scene, IceSite site, IceClimate climate, WaterShoreField.Snapshot field, uint seed,
			IIcePrefabSource prefabs, IcePlacerOptions options = null)
		{
			options ??= new IcePlacerOptions();
			var report = new IcePlacerReport { Climate = climate };
			int removed = Clear(scene);
			if (removed > 1)
			{
				report.Notes.Add($"Sea ice: {removed} '{RootName}' roots were found and replaced by one.");
			}
			if (!climate.AnyIce)
			{
				if (climate.NoIceReason != null)
				{
					report.Notes.Add(climate.NoIceReason);
				}
				return report;
			}
			if (field == null || field.Resolution < 2)
			{
				report.Notes.Add("Sea ice: the sea has no depth field (no terrain under it?), so no ice was placed — a berg could not be kept off the bed.");
				return report;
			}
			if (prefabs == null)
			{
				report.Notes.Add("Sea ice: no prefab source; none placed.");
				return report;
			}

			report.SeaKm2 = OpenSeaKm2(field, site, options);
			if (report.SeaKm2 <= 0f)
			{
				report.Notes.Add("Sea ice: no open sea clear of the shore and the scene's edge; none placed.");
				return report;
			}

			var rng = new IceRandom(seed ^ 0x51CE1CEBu);
			var plan = new List<Planned>();
			PlanBergs(climate, report, plan, ref rng, options);
			PlanSeaIce(climate, report, plan, prefabs, ref rng, options);

			var placed = new List<Disc>();
			var grid = new Dictionary<long, List<int>>();
			Transform root = null;
			foreach (Planned piece in plan)
			{
				if (!prefabs.TryMeasure(piece.Prefab, out Bounds bounds))
				{
					Count(report.Missing, piece.Prefab);
					continue;
				}
				if (!TryPosition(piece, bounds, field, site, placed, grid, ref rng, options, report,
					out Vector2 centre, out float yaw, out IceFooting footing, out float rise))
				{
					Count(report.NoRoom, piece.Prefab);
					continue;
				}
				if (root == null)
				{
					var host = new GameObject(RootName);
					SceneManager.MoveGameObjectToScene(host, scene);
					host.AddComponent<GeneratedSeaIce>();
					root = host.transform;
					report.Root = host;
				}
				GameObject instance = prefabs.Instantiate(piece.Prefab, root);
				if (instance == null)
				{
					Count(report.Missing, piece.Prefab);
					continue;
				}
				instance.name = $"{piece.Prefab} {report.Bergs + report.SeaIce + 1}";
				instance.transform.SetPositionAndRotation(new Vector3(centre.x, site.SeaLevelY + rise, centre.y), Quaternion.Euler(0f, yaw, 0f));
				WaterFloater floater = instance.GetComponent<WaterFloater>();
				if (floater != null)
				{
					if (site.Surface != null)
					{
						floater.Surface = site.Surface;
					}
					if (footing == IceFooting.Grounded)
					{
						// Aground: a keel on the bed does not heave, pitch or roll with the sea.
						floater.Response = 0f;
					}
				}
				else if (footing == IceFooting.Floats)
				{
					report.Notes.Add($"Sea ice: '{piece.Prefab}' has no WaterFloater, so it stands still on the sea.");
				}
				Count(report.Placed, piece.Prefab);
				if (footing == IceFooting.Grounded)
				{
					report.Grounded++;
				}
				if (piece.Berg)
				{
					report.Bergs++;
				}
				else
				{
					report.SeaIce++;
				}
				Insert(grid, placed, new Disc { Centre = centre, Radius = Radius(bounds), Berg = piece.Berg }, options.CellMetres);
			}

			report.Notes.Add(report.ToString());
			if (report.Missing.Count > 0)
			{
				report.Notes.Add($"Sea ice: prefabs not generated yet, so left out (run Generate Biome Art): {Join(report.Missing)}.");
			}
			if (report.NoRoom.Count > 0)
			{
				report.Notes.Add($"Sea ice: no position passed the depth, shore and spacing tests for: {Join(report.NoRoom)}.");
			}
			if (report.SeaIceCapped)
			{
				report.Notes.Add($"Sea ice: the pack ({climate.SeaIceConcentration:0.00} concentration) is thinned to at most " +
					$"{options.MaxSeaIceCoverage:0.##} cover or {options.MaxSeaIcePieces} pieces so the sea stays navigable.");
			}
			return report;
		}

		// ── Planning ──────────────────────────────────────────────────

		private static void PlanBergs(in IceClimate climate, IcePlacerReport report, List<Planned> plan, ref IceRandom rng, IcePlacerOptions options)
		{
			if (!climate.HasBergs)
			{
				return;
			}
			int total = 0;
			for (int s = (int)IcebergSize.Large; s >= (int)IcebergSize.Growler; s--)
			{
				var size = (IcebergSize)s;
				float expected = IceOccurrence.BergsPerKm2(size, climate.CalvingFront) * report.SeaKm2 * climate.BergSupply;
				// Drawn even when too large, so a warmer climate does not reshuffle the smaller sizes' draws.
				int count = Draw(expected, ref rng);
				float pick = rng.NextFloat();
				if (size > climate.LargestBerg.Value)
				{
					continue;
				}
				count = Math.Min(count, options.MaxBergs - total);
				for (int i = 0; i < count; i++)
				{
					string prefab = PickBerg(size, climate, i == 0 ? pick : rng.NextFloat());
					if (prefab != null)
					{
						plan.Add(new Planned { Prefab = prefab, Berg = true });
						total++;
					}
				}
			}
		}

		private static string PickBerg(IcebergSize size, in IceClimate climate, float pick)
		{
			float sum = 0f;
			foreach (IcebergShape shape in IceMeshes.Icebergs)
			{
				if (shape.Size == size)
				{
					sum += IceOccurrence.ClassWeight(shape.Class, climate.SourceAnnualC, climate.SeaSurfaceC, climate.DriftDegrees);
				}
			}
			if (sum <= 0f)
			{
				return null;
			}
			float target = pick * sum;
			string last = null;
			foreach (IcebergShape shape in IceMeshes.Icebergs)
			{
				if (shape.Size != size)
				{
					continue;
				}
				float w = IceOccurrence.ClassWeight(shape.Class, climate.SourceAnnualC, climate.SeaSurfaceC, climate.DriftDegrees);
				if (w <= 0f)
				{
					continue;
				}
				last = IcebergPrefab(shape);
				target -= w;
				if (target <= 0f)
				{
					return last;
				}
			}
			return last;
		}

		private static void PlanSeaIce(in IceClimate climate, IcePlacerReport report, List<Planned> plan, IIcePrefabSource prefabs,
			ref IceRandom rng, IcePlacerOptions options)
		{
			if (!climate.HasSeaIce)
			{
				return;
			}
			float pancakes = IceOccurrence.PancakeShare(climate.SeaIceConcentration);
			int pancakeShapes = 0, floeShapes = 0;
			foreach (SeaIceShape shape in IceMeshes.SeaIce)
			{
				if (shape.Kind == SeaIceKind.Pancake) pancakeShapes++; else floeShapes++;
			}
			// Share of pieces by number, and the mean area of one, from the measured prefabs where there are some.
			var shares = new float[IceMeshes.SeaIce.Length];
			float meanArea = 0f;
			for (int i = 0; i < shares.Length; i++)
			{
				SeaIceShape shape = IceMeshes.SeaIce[i];
				shares[i] = shape.Kind == SeaIceKind.Pancake
					? pancakes / Math.Max(1, pancakeShapes)
					: (1f - pancakes) / Math.Max(1, floeShapes);
				float area = prefabs.TryMeasure(SeaIcePrefab(shape), out Bounds b)
					? Mathf.PI * 0.25f * b.size.x * b.size.z
					: Mathf.PI * 0.25f * shape.Length * shape.Width;
				meanArea += shares[i] * area;
			}
			if (meanArea <= 0f)
			{
				return;
			}
			float coverage = Mathf.Min(climate.SeaIceConcentration, options.MaxSeaIceCoverage);
			float wanted = climate.SeaIceConcentration * report.SeaKm2 * 1e6f / meanArea;
			float target = Mathf.Min(coverage * report.SeaKm2 * 1e6f / meanArea, options.MaxSeaIcePieces);
			report.SeaIceCapped = wanted > target + 0.5f;
			// Largest first: they are the hardest to fit.
			var order = new List<int>();
			for (int i = 0; i < shares.Length; i++) order.Add(i);
			order.Sort((a, b) => (IceMeshes.SeaIce[b].Length * IceMeshes.SeaIce[b].Width).CompareTo(IceMeshes.SeaIce[a].Length * IceMeshes.SeaIce[a].Width));
			foreach (int i in order)
			{
				int count = Draw(target * shares[i], ref rng);
				string prefab = SeaIcePrefab(IceMeshes.SeaIce[i]);
				for (int k = 0; k < count; k++)
				{
					plan.Add(new Planned { Prefab = prefab, Berg = false });
				}
			}
		}

		private static int Draw(float expected, ref IceRandom rng)
		{
			expected = Mathf.Max(0f, expected);
			int whole = Mathf.FloorToInt(expected);
			return whole + (rng.NextFloat() < expected - whole ? 1 : 0);
		}

		// ── Positions ─────────────────────────────────────────────────

		/// <summary>The footprint's circle about the pivot, m: the farthest corner of its bounds in plan.</summary>
		private static float Radius(Bounds b)
		{
			float x = Mathf.Max(Mathf.Abs(b.min.x), Mathf.Abs(b.max.x));
			float z = Mathf.Max(Mathf.Abs(b.min.z), Mathf.Abs(b.max.z));
			return Mathf.Sqrt(x * x + z * z);
		}

		private static bool TryPosition(Planned piece, Bounds bounds, WaterShoreField.Snapshot field, IceSite site, List<Disc> placed,
			Dictionary<long, List<int>> grid, ref IceRandom rng, IcePlacerOptions options, IcePlacerReport report,
			out Vector2 centre, out float yaw, out IceFooting footing, out float rise)
		{
			centre = default;
			yaw = 0f;
			footing = IceFooting.Rejected;
			rise = 0f;
			float radius = Radius(bounds);
			float inset = options.EdgeMarginMetres + radius;
			Rect area = site.Area;
			float shoreGap = piece.Berg ? options.BergShoreGapMetres : options.SeaIceShoreGapMetres;
			float draught = Mathf.Max(0f, -bounds.min.y);
			for (int attempt = 0; attempt < options.AttemptsPerPiece; attempt++)
			{
				// Always three draws per attempt, so one rejection reason never shifts another piece's draws.
				float u = rng.NextFloat(), v = rng.NextFloat(), w = rng.NextFloat();
				if (area.width <= 2f * inset || area.height <= 2f * inset)
				{
					Count(report.Rejections, "scene too small");
					return false;
				}
				var c = new Vector2(Mathf.Lerp(area.xMin + inset, area.xMax - inset, u), Mathf.Lerp(area.yMin + inset, area.yMax - inset, v));
				float angle = w * 360f;
				if (Conflicts(grid, placed, c, radius, piece.Berg, options))
				{
					Count(report.Rejections, "spacing");
					continue;
				}
				if (field.OpenSeaAt(c, 0f) < options.MinOpenSea)
				{
					Count(report.Rejections, "sheltered water");
					continue;
				}
				if (!Footprint(field, bounds, c, angle, options, out float minClearance, out float minEdge))
				{
					Count(report.Rejections, "off the depth field");
					continue;
				}
				if (minEdge < shoreGap)
				{
					Count(report.Rejections, "near the shore");
					continue;
				}
				IceFooting f = Footing(minClearance, draught, site.LowTideMetres, options, out float r);
				if (f == IceFooting.Rejected)
				{
					Count(report.Rejections, "too shallow");
					continue;
				}
				centre = c;
				yaw = angle;
				footing = f;
				rise = r;
				return true;
			}
			return false;
		}

		/// <summary>
		/// Whether a piece floats, sits on the bed or cannot go where its keel has this clearance.
		/// </summary>
		/// <param name="minClearance">Least depth at mean sea level less the keel's depth, over the footprint, m (negative: the bed is above the keel).</param>
		/// <param name="draught">The piece's deepest point below its waterline, m.</param>
		/// <param name="lowTide">How far below mean sea level the tide falls, m.</param>
		/// <param name="rise">How far above its float line a grounded piece stands, m.</param>
		public static IceFooting Footing(float minClearance, float draught, float lowTide, IcePlacerOptions options, out float rise)
		{
			options ??= new IcePlacerOptions();
			rise = 0f;
			if (minClearance >= Mathf.Max(0f, lowTide) + options.HeaveAllowanceMetres + options.KeelMarginMetres)
			{
				return IceFooting.Floats;
			}
			rise = Mathf.Max(0f, -minClearance);
			return rise <= options.MaxGroundedRise * Mathf.Max(0f, draught) ? IceFooting.Grounded : IceFooting.Rejected;
		}

		/// <summary>
		/// Depth of a half-ellipsoid keel at a point of the plan, m: the draught at the bounds' centre,
		/// nothing at the edge of the inscribed ellipse.
		/// </summary>
		public static float KeelDepth(Bounds bounds, float localX, float localZ)
		{
			float draught = Mathf.Max(0f, -bounds.min.y);
			float ex = Mathf.Max(1e-3f, bounds.extents.x), ez = Mathf.Max(1e-3f, bounds.extents.z);
			float a = (localX - bounds.center.x) / ex, b = (localZ - bounds.center.z) / ez;
			return draught * Mathf.Sqrt(Mathf.Max(0f, 1f - a * a - b * b));
		}

		/// <summary>
		/// The footprint against the field, rotated as placed: the least clearance under the keel and
		/// the least distance to the waterline over every sample. False when any sample is off the field.
		/// </summary>
		public static bool Footprint(WaterShoreField.Snapshot field, Bounds bounds, Vector2 centre, float yawDegrees, IcePlacerOptions options,
			out float minClearance, out float minEdge)
		{
			options ??= new IcePlacerOptions();
			minClearance = float.MaxValue;
			minEdge = float.MaxValue;
			float texel = Mathf.Max(0.25f, field.TexelMetres);
			// Odd counts, so the centre — where the keel is deepest — is always one of the samples.
			int most = Mathf.Max(3, options.FootprintSamples) | 1;
			int nx = Mathf.Clamp(Mathf.CeilToInt(bounds.size.x / texel) + 1, 3, most) | 1;
			int nz = Mathf.Clamp(Mathf.CeilToInt(bounds.size.z / texel) + 1, 3, most) | 1;
			// Unity's yaw turns +x toward −z: (x, z) → (x cos + z sin, −x sin + z cos).
			float rad = yawDegrees * Mathf.Deg2Rad, cos = Mathf.Cos(rad), sin = Mathf.Sin(rad);
			for (int j = 0; j < nz; j++)
			{
				float lz = Mathf.Lerp(bounds.min.z, bounds.max.z, j / (float)(nz - 1));
				for (int i = 0; i < nx; i++)
				{
					float lx = Mathf.Lerp(bounds.min.x, bounds.max.x, i / (float)(nx - 1));
					var p = new Vector2(centre.x + lx * cos + lz * sin, centre.y - lx * sin + lz * cos);
					if (!field.TrySample(p, out float depth, out float edge))
					{
						return false;
					}
					minClearance = Mathf.Min(minClearance, depth - KeelDepth(bounds, lx, lz));
					minEdge = Mathf.Min(minEdge, edge);
				}
			}
			return true;
		}

		/// <summary>Open sea the ice may use, km²: wet, open to the waves, clear of the shore and the edge.</summary>
		public static float OpenSeaKm2(WaterShoreField.Snapshot field, IceSite site, IcePlacerOptions options)
		{
			options ??= new IcePlacerOptions();
			float step = Mathf.Max(1f, options.SeaSampleMetres);
			Rect area = site.Area;
			float m = options.EdgeMarginMetres;
			int count = 0;
			for (float z = area.yMin + m + step * 0.5f; z < area.yMax - m; z += step)
			{
				for (float x = area.xMin + m + step * 0.5f; x < area.xMax - m; x += step)
				{
					var p = new Vector2(x, z);
					if (field.TrySample(p, out float depth, out float edge) && depth > 0f && edge >= options.SeaIceShoreGapMetres
						&& field.OpenSeaAt(p, 0f) >= options.MinOpenSea)
					{
						count++;
					}
				}
			}
			return count * step * step * 1e-6f;
		}

		private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

		private static bool Conflicts(Dictionary<long, List<int>> grid, List<Disc> placed, Vector2 c, float radius, bool berg, IcePlacerOptions options)
		{
			float cell = Mathf.Max(1f, options.CellMetres);
			float reach = radius + Mathf.Max(options.BergGapMetres, options.SeaIceGapMetres);
			int x0 = Mathf.FloorToInt((c.x - reach) / cell), x1 = Mathf.FloorToInt((c.x + reach) / cell);
			int z0 = Mathf.FloorToInt((c.y - reach) / cell), z1 = Mathf.FloorToInt((c.y + reach) / cell);
			for (int z = z0; z <= z1; z++)
			{
				for (int x = x0; x <= x1; x++)
				{
					if (!grid.TryGetValue(Key(x, z), out List<int> list))
					{
						continue;
					}
					foreach (int i in list)
					{
						Disc d = placed[i];
						float gap = berg || d.Berg ? options.BergGapMetres : options.SeaIceGapMetres;
						float need = radius + d.Radius + gap;
						if ((d.Centre - c).sqrMagnitude < need * need)
						{
							return true;
						}
					}
				}
			}
			return false;
		}

		private static void Insert(Dictionary<long, List<int>> grid, List<Disc> placed, Disc disc, float cellMetres)
		{
			int index = placed.Count;
			placed.Add(disc);
			float cell = Mathf.Max(1f, cellMetres);
			int x0 = Mathf.FloorToInt((disc.Centre.x - disc.Radius) / cell), x1 = Mathf.FloorToInt((disc.Centre.x + disc.Radius) / cell);
			int z0 = Mathf.FloorToInt((disc.Centre.y - disc.Radius) / cell), z1 = Mathf.FloorToInt((disc.Centre.y + disc.Radius) / cell);
			for (int z = z0; z <= z1; z++)
			{
				for (int x = x0; x <= x1; x++)
				{
					long key = Key(x, z);
					if (!grid.TryGetValue(key, out List<int> list))
					{
						grid[key] = list = new List<int>();
					}
					list.Add(index);
				}
			}
		}

		private static void Count(SortedDictionary<string, int> counts, string key)
		{
			counts.TryGetValue(key, out int n);
			counts[key] = n + 1;
		}

		private static string Join(SortedDictionary<string, int> counts)
		{
			var parts = new List<string>();
			foreach (KeyValuePair<string, int> kv in counts)
			{
				parts.Add($"{kv.Key} ×{kv.Value}");
			}
			return string.Join(", ", parts);
		}

		/// <summary>SplitMix64: a fixed, platform-independent stream from the scene's seed.</summary>
		private struct IceRandom
		{
			private ulong state;

			public IceRandom(uint seed)
			{
				state = 0x9E3779B97F4A7C15ul ^ seed;
			}

			public float NextFloat()
			{
				state += 0x9E3779B97F4A7C15ul;
				ulong z = state;
				z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
				z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
				z ^= z >> 31;
				return (z >> 40) * (1f / 16777216f);
			}
		}
	}
}
#endif
