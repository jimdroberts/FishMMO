using System;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// The climate of a scene that stands on a world: the body's own <see cref="PlanetClimateField"/>,
	/// resolved once per scene, asked at each position's true place on the globe and true altitude.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One climate model, not two.</b> A generated scene's biomes were painted by
	/// <see cref="PlanetClimateField.At(Vector3, float)"/> at each point's real direction on the
	/// globe and its real altitude — the scene's world Y over its vertical scale. The running scene
	/// used to read a different model altogether: an authored <see cref="ClimateSettings"/>
	/// evaluated at a height normalised over the scene's own relief, plus the body's mean offsets at
	/// a latitude spread across the map by an authored span (20° by default, across a scene a
	/// couple of kilometres wide). So the lowest ground of every scene read as sea floor and its
	/// highest as a summit whatever they really were, the weather and the climate variants
	/// disagreed with the ground, and a biome chosen at runtime could contradict the painted one.
	/// A generated scene now evaluates exactly the field the generator painted from:
	/// <see cref="SampleAt"/> is <see cref="PlanetClimateField.ClimateAt"/> at
	/// <see cref="AtlasGeometry.SceneToUnit"/> of the position and at world Y ÷
	/// <see cref="VerticalScale"/>, the same conversion <c>SceneBiomeField</c> makes.
	/// </para>
	/// <para>
	/// <b>Cheap per sample.</b> The biome sampler runs per character per second on the server and
	/// the owning client alike. The field (mean temperature, lapse rate, hypsometry, moisture model)
	/// is built once here and kept; a sample is one direction, the regional noise and one latitude
	/// temperature — no allocation. The moisture walk is the one costly term, a few dozen noise
	/// evaluations, and it depends on the direction alone, so it is sampled once on a
	/// <see cref="GridSize"/>² grid over the footprint and read back bilinearly. At the grid's own
	/// points the sample IS the generator's; between them only the humidity's wind-driven share is
	/// interpolated, over a thirty-second of the scene.
	/// </para>
	/// <para>
	/// <b>Nothing on the wire.</b> Server and client each build this from the same atlas entry and
	/// the same body through the same deterministic functions, and get the same numbers.
	/// </para>
	/// <para>
	/// A hand-made scene that is merely placed (<see cref="WorldAtlasScene.CutRadiusKm"/> 0) keeps
	/// its authored climate — its map was painted to that, not to the globe — and takes only the
	/// moisture at its centre (<see cref="CentreMoisture"/>), so it still knows whether it is on a
	/// wet coast or in a desert.
	/// </para>
	/// </remarks>
	public sealed class ScenePlacementClimate
	{
		/// <summary>Grid points per side over a generated scene's footprint, for the moisture term.</summary>
		/// <remarks>
		/// 33: a scene cut at a small atlas radius spans several degrees of its globe, enough for a
		/// coastline or a rain shadow's edge to cross it, and 32 cells across keeps such an edge within
		/// a thirty-second of the scene of where the generator drew it. Building it is 1,089 moisture
		/// walks — a few milliseconds, once per scene load.
		/// </remarks>
		public const int GridSize = 33;

		// What it was built from: any change rebuilds it.
		private readonly SolarSystemProfile system;
		private readonly WorldBody body;
		private readonly AtlasFootprint footprint;
		private readonly double radiusKm;

		/// <summary>The body's climate field, resolved once. What the generator painted from.</summary>
		private readonly PlanetClimateField field;

		/// <summary>True when the scene was cut from the globe, so every point has its own place and altitude on it.</summary>
		public readonly bool Generated;

		/// <summary>Scene metres per metre of the planet's altitude: <see cref="PlanetSurface.SceneVerticalScale"/> at the cut radius.</summary>
		/// <remarks>1 for a scene that is only placed: its Y was never scaled from anything.</remarks>
		public readonly float VerticalScale;

		/// <summary>The moisture term at the scene's centre: all a hand-made scene uses.</summary>
		public readonly float CentreMoisture;

		/// <summary>What the body offers a biome, from the same field: the resolver's physical filter.</summary>
		public readonly BiomeWorldConditions Conditions;

		private readonly float[] moisture;

		private ScenePlacementClimate(SolarSystemProfile system, WorldBody body, in AtlasFootprint footprint, double radiusKm, bool generated)
		{
			this.system = system;
			this.body = body;
			this.footprint = footprint;
			this.radiusKm = radiusKm;
			Generated = generated;

			field = PlanetClimateField.For(system, body);
			Conditions = field.Conditions;
			VerticalScale = generated ? Mathf.Max(1e-6f, PlanetSurface.SceneVerticalScale(body, radiusKm)) : 1f;
			Vector3 centre = AtlasGeometry.ToUnit(footprint.Latitude, footprint.Longitude).ToVector3();
			CentreMoisture = field.Moisture.Anomaly(footprint.Latitude, centre);
			if (!generated)
			{
				return;
			}

			moisture = new float[GridSize * GridSize];
			for (int j = 0; j < GridSize; j++)
			{
				for (int i = 0; i < GridSize; i++)
				{
					NodeKm(i, j, out double xKm, out double zKm);
					Vector3 direction = AtlasGeometry.SceneToUnit(footprint, xKm, zKm, radiusKm).ToVector3().normalized;
					// The latitude and direction PlanetClimateField.At(direction, altitude) itself takes.
					moisture[j * GridSize + i] = field.Moisture.Anomaly(PlanetClimateField.LatitudeOf(direction), direction);
				}
			}
		}

		/// <summary>
		/// The placement climate of a scene placed on a body, or null when it has no place on one.
		/// </summary>
		/// <param name="system">The solar system, as <see cref="SolarSystemProfile.Active"/> gives it.</param>
		/// <param name="entry">The scene's atlas entry.</param>
		public static ScenePlacementClimate For(SolarSystemProfile system, WorldAtlasScene entry)
		{
			if (entry == null || !entry.Placed)
			{
				return null;
			}
			return For(system, ResolveBody(system, entry), AtlasFootprint.Of(entry), entry.CutRadiusKm, entry.CutRadiusKm > 0f);
		}

		/// <summary>The same from its parts, for tests and tools.</summary>
		/// <param name="radiusKm">The atlas radius the scene was cut at. Ignored unless <paramref name="generated"/>.</param>
		public static ScenePlacementClimate For(SolarSystemProfile system, WorldBody body, in AtlasFootprint footprint, double radiusKm, bool generated)
		{
			return new ScenePlacementClimate(system, body, footprint, Math.Max(1e-3, radiusKm), generated && radiusKm > 0.0);
		}

		/// <summary>
		/// The body a placed scene stands on: its entry's, else the home world, which is what an
		/// empty body means on the atlas.
		/// </summary>
		public static WorldBody ResolveBody(SolarSystemProfile system, WorldAtlasScene entry)
		{
			if (entry != null && entry.Body != null)
			{
				return entry.Body;
			}
			return system != null ? system.HomeWorld : null;
		}

		/// <summary>True when this was built from exactly this placement, so it still answers for it.</summary>
		/// <remarks>
		/// Compared on every read rather than invalidated by events: an entry edited in the
		/// designer, a body reassigned, or the active system loading late all simply mismatch and
		/// rebuild. A dozen comparisons, against a field and a grid of moisture walks.
		/// </remarks>
		public bool Matches(SolarSystemProfile system, WorldAtlasScene entry)
		{
			if (entry == null || !entry.Placed)
			{
				return false;
			}
			bool generated = entry.CutRadiusKm > 0f;
			return ReferenceEquals(this.system, system)
				&& ReferenceEquals(body, ResolveBody(system, entry))
				&& Generated == generated
				&& footprint.Latitude == entry.Latitude
				&& footprint.Longitude == entry.Longitude
				&& footprint.HeadingDegrees == entry.HeadingDegrees
				&& footprint.SizeKm == entry.SizeKm
				&& (!generated || radiusKm == Math.Max(1e-3, entry.CutRadiusKm));
		}

		/// <summary>
		/// The unit direction from the body's centre of a scene position, as the generator laid the
		/// scene over the globe.
		/// </summary>
		/// <param name="worldX">Scene metres east of its centre (before heading).</param>
		/// <param name="worldZ">Scene metres north of its centre (before heading).</param>
		public Vector3 DirectionAt(float worldX, float worldZ)
		{
			if (!Generated)
			{
				return AtlasGeometry.ToUnit(footprint.Latitude, footprint.Longitude).ToVector3();
			}
			return AtlasGeometry.SceneToUnit(footprint, worldX / 1000.0, worldZ / 1000.0, radiusKm).ToVector3().normalized;
		}

		/// <summary>The planet's altitude, in its own metres, of a scene world Y: Y ÷ <see cref="VerticalScale"/>.</summary>
		public float AltitudeOf(float worldY) => worldY / VerticalScale;

		/// <summary>
		/// The true latitude of a scene position, degrees: through the footprint for a generated
		/// scene, the scene's centre latitude otherwise.
		/// </summary>
		public double LatitudeAt(float worldX, float worldZ)
		{
			if (!Generated)
			{
				return footprint.Latitude;
			}
			return PlanetClimateField.LatitudeOf(DirectionAt(worldX, worldZ));
		}

		/// <summary>
		/// The climate at a position in a generated scene: the body's field at the position's place on
		/// the globe and at its altitude, exactly as the generator painted from.
		/// </summary>
		/// <param name="worldPosition">
		/// A scene position; its Y is taken as the altitude, so pass the ground under a character (as
		/// <see cref="BiomeSampler"/> does) for the ground's climate, or Y = 0 for sea level.
		/// </param>
		/// <param name="normalizedHeight">The height as the biome system reads it, planet-relative: what the generator chose the tier from.</param>
		/// <remarks>
		/// Only meaningful for a <see cref="Generated"/> scene; a merely placed one keeps its authored
		/// climate and is answered by <see cref="WorldSceneSettings"/>. Allocation-free.
		/// </remarks>
		public ClimateSample SampleAt(Vector3 worldPosition, out float normalizedHeight)
		{
			// Unnormalised, exactly as the generator hands it to PlanetClimateField.At, which
			// normalises it once — as ClimateAt does — so the two agree to the last bit.
			Vector3 direction = Generated
				? AtlasGeometry.SceneToUnit(footprint, worldPosition.x / 1000.0, worldPosition.z / 1000.0, radiusKm).ToVector3()
				: AtlasGeometry.ToUnit(footprint.Latitude, footprint.Longitude).ToVector3();
			return field.ClimateAt(direction, AltitudeOf(worldPosition.y), MoistureAt(worldPosition.x, worldPosition.z), out normalizedHeight);
		}

		/// <summary>
		/// The biome the generator would paint at a position of a generated scene: the field's own choice
		/// (<see cref="PlanetClimateField.SelectBiome"/>, selection noise and summer included) from the
		/// honest climate there — never one shifted by today's weather or season.
		/// </summary>
		/// <remarks>
		/// For a position the baked map does not cover (2026-10-10). Choosing from the weather-shifted
		/// sample with no selection noise made ground off the map change biome with the season and
		/// disagree with the painted ground at every ecotone.
		/// </remarks>
		public BiomeTemplate SelectBiomeAt(Vector3 worldPosition)
		{
			Vector3 direction = Generated
				? AtlasGeometry.SceneToUnit(footprint, worldPosition.x / 1000.0, worldPosition.z / 1000.0, radiusKm).ToVector3().normalized
				: AtlasGeometry.ToUnit(footprint.Latitude, footprint.Longitude).ToVector3().normalized;
			float altitude = AltitudeOf(worldPosition.y);
			ClimateSample climate = field.ClimateAt(direction, altitude, MoistureAt(worldPosition.x, worldPosition.z), out float normalized);
			var point = new PlanetSurfacePoint { AltitudeMetres = altitude, NormalizedHeight = normalized, Climate = climate };
			return field.SelectBiome(direction, point);
		}

		/// <summary>
		/// The wind-driven moisture at a scene position, from the grid: exact at its nodes, bilinear
		/// between them; <see cref="CentreMoisture"/> for a scene that is only placed.
		/// </summary>
		public float MoistureAt(float worldX, float worldZ)
		{
			if (!Generated)
			{
				return CentreMoisture;
			}
			/* Outside the footprint the edge holds: a character walking past the boundary is still
			 * in this scene's climate, and extrapolating a moisture gradient would be inventing one. */
			float sizeX = Mathf.Max(1e-6f, footprint.SizeKm.x), sizeZ = Mathf.Max(1e-6f, footprint.SizeKm.y);
			float u = Mathf.Clamp((worldX / 1000f / sizeX + 0.5f) * (GridSize - 1), 0f, GridSize - 1);
			float v = Mathf.Clamp((worldZ / 1000f / sizeZ + 0.5f) * (GridSize - 1), 0f, GridSize - 1);
			int i = Mathf.Min((int)u, GridSize - 2), j = Mathf.Min((int)v, GridSize - 2);
			float fu = u - i, fv = v - j;
			int n00 = j * GridSize + i, n10 = n00 + 1, n01 = n00 + GridSize, n11 = n01 + 1;
			return Mathf.Lerp(Mathf.Lerp(moisture[n00], moisture[n10], fu), Mathf.Lerp(moisture[n01], moisture[n11], fu), fv);
		}

		/// <summary>The scene-km position of grid node (i, j): the footprint's own frame, centred on the scene.</summary>
		public void NodeKm(int i, int j, out double xKm, out double zKm)
		{
			xKm = (i / (double)(GridSize - 1) - 0.5) * footprint.SizeKm.x;
			zKm = (j / (double)(GridSize - 1) - 0.5) * footprint.SizeKm.y;
		}
	}
}
